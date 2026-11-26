using System.Diagnostics;
using System.Text.Json;
using FoodVision.Api.Domain;
using FoodVision.Api.Utilites;
using Microsoft.Extensions.Options;

namespace FoodVision.Api.Infrastructure;

internal sealed class VideoAnalyzer(InferenceRuntime inferenceRuntime, IOptions<AnalysisOptions> configured, ILogger<VideoAnalyzer> logger) : IVideoAnalyzer
{
    private readonly AnalysisOptions _options = configured.Value;

    public async Task<IReadOnlyList<TableConfiguration>> ProposeTables(IFormFile video, CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"foodvision-{Guid.NewGuid():N}{Path.GetExtension(video.FileName)}");
        try
        {
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await video.CopyToAsync(destination, cancellationToken);
            var (sourceWidth, sourceHeight, _) = await ReadMetadata(temporaryPath, cancellationToken);
            var ratio = Math.Min((double)_options.FrameSize / sourceWidth, (double)_options.FrameSize / sourceHeight);
            var width = Math.Max(2, (int)(sourceWidth * ratio) / 2 * 2);
            var height = Math.Max(2, (int)(sourceHeight * ratio) / 2 * 2);
            var model = inferenceRuntime.TableDetector;
            using var decoder = StartFfmpeg(temporaryPath, width, height, .25);
            var errorTask = decoder.StandardError.ReadToEndAsync(cancellationToken);
            var frame = new byte[checked(width * height * 3)];
            var proposer = new TableLayoutProposer(width, height);
            try
            {
                while (await ReadFrame(decoder.StandardOutput.BaseStream, frame, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var gpuLease = await GpuInferenceGate.EnterInferenceAsync(cancellationToken);
                    using var inputLease = inferenceRuntime.RentInput();
                    var workspace = inputLease.Workspace;
                    workspace.Prepare(frame, width, height);
                    proposer.AddFrame(model.Detect(workspace.TableInput, .20f, classId: -1));
                }
            }
            finally { if (!decoder.HasExited) decoder.Kill(entireProcessTree: true); }
            var error = await errorTask;
            await decoder.WaitForExitAsync(cancellationToken);
            if (decoder.ExitCode != 0) throw new InvalidOperationException($"Не удалось подготовить видео: {error.Trim()}");
            return proposer.Build();
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { logger.LogWarning(exception, "Could not delete temporary video {Path}", temporaryPath); }
        }
    }

    public Task<VideoAnalysis> Analyze(IFormFile video, IReadOnlyList<TableConfiguration> configuredTables, CancellationToken cancellationToken) =>
        Analyze(video, configuredTables, null, cancellationToken);

    public async Task<VideoAnalysis> Analyze(IFormFile video, IReadOnlyList<TableConfiguration> configuredTables, IReadOnlyList<TableOrderInterval>? orders, CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"foodvision-{Guid.NewGuid():N}{Path.GetExtension(video.FileName)}");
        try
        {
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await video.CopyToAsync(destination, cancellationToken);

            var (sourceWidth, sourceHeight, duration) = await ReadMetadata(temporaryPath, cancellationToken);
            var ratio = Math.Min((double)_options.FrameSize / sourceWidth, (double)_options.FrameSize / sourceHeight);
            var width = Math.Max(2, (int)(sourceWidth * ratio) / 2 * 2);
            var height = Math.Max(2, (int)(sourceHeight * ratio) / 2 * 2);

            var tablesModel = inferenceRuntime.TableDetector;
            var peopleModel = inferenceRuntime.PersonPose;
            var personDetector = inferenceRuntime.PersonDetector;
            var rfdetrModel = inferenceRuntime.RfDetrKeypoint;
            using var decoder = StartFfmpeg(temporaryPath, width, height);
            var decoderErrorTask = decoder.StandardError.ReadToEndAsync(cancellationToken);
            var frame = new byte[checked(width * height * 3)];
            var tableTracks = configuredTables.Select(table => new TableTrack(table.Id,
                new Box((float)(table.Bounds.X * width), (float)(table.Bounds.Y * height), (float)(table.Bounds.Width * width), (float)(table.Bounds.Height * height)),
                table.SeatingZones.Select(zone => new Box((float)(zone.X * width), (float)(zone.Y * height), (float)(zone.Width * width), (float)(zone.Height * height))).ToArray())).ToList();
            var frameIndex = 0;
            var frameDuration = 1 / Math.Max(.1, _options.SamplingFramesPerSecond);

            try
            {
                while (await ReadFrame(decoder.StandardOutput.BaseStream, frame, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var seconds = frameIndex * frameDuration;
                    IReadOnlyList<Detection> objectDetections;
                    IReadOnlyList<Detection> people = Array.Empty<Detection>();
                    using (var gpuLease = await GpuInferenceGate.EnterInferenceAsync(cancellationToken))
                    using (var inputLease = inferenceRuntime.RentInput())
                    {
                        var workspace = inputLease.Workspace;
                        workspace.Prepare(frame, width, height);
                        objectDetections = tablesModel.Detect(workspace.TableInput, .1f, classId: -1);
                        if (tableTracks.Count > 0)
                        {
                            if (rfdetrModel is not null && workspace.RfDetrKeypointInput is not null)
                            {
                                people = rfdetrModel.Detect(workspace.RfDetrKeypointInput);
                            }
                            else if (peopleModel is not null)
                            {
                                var poses = peopleModel.Detect(workspace.PoseInput, .08f, classId: 0);
                                people = personDetector is not null && workspace.PersonInput is not null
                                    ? personDetector.Detect(workspace.PersonInput, poses)
                                    : poses;
                            }
                        }
                    }
                    var tableDetections = objectDetections.Where(detection => detection.ClassId == 60).ToArray();
                    if (tableTracks.Count > 0)
                    {
                        var belongings = objectDetections.Where(detection => detection.ClassId is 24 or 26 or 28).ToArray();
                        var assignedPeople = TablePersonAssignment.Assign(people,
                            tableTracks.Select(table => new TableAssociation(table.Bounds, table.SeatingZones)).ToArray());
                        for (var tableIndex = 0; tableIndex < tableTracks.Count; tableIndex++)
                        {
                            var table = tableTracks[tableIndex];
                            var hasActiveOrder = orders != null && orders.Any(o =>
                                string.Equals(o.TableId, table.Id, StringComparison.OrdinalIgnoreCase) &&
                                seconds >= o.StartSeconds &&
                                (!o.EndSeconds.HasValue || seconds <= o.EndSeconds.Value));
                            var visible = tableDetections.Any(detection => Geometry.IoU(table.Bounds, detection.Box) >= .2f);
                            var state = table.Seating.Classify(
                                seconds,
                                assignedPeople.People[tableIndex],
                                belongings,
                                visible,
                                assignedPeople.Ambiguous[tableIndex],
                                hasActiveOrder);
                            table.Samples.Add((seconds, state));
                        }
                    }
                    frameIndex++;
                }
            }
            finally { if (!decoder.HasExited) decoder.Kill(entireProcessTree: true); }

            var error = await decoderErrorTask;
            await decoder.WaitForExitAsync(cancellationToken);
            if (decoder.ExitCode != 0) throw new InvalidOperationException($"Не удалось прочитать видео: {error.Trim()}");
            if (frameIndex == 0) throw new InvalidOperationException("В видео не найдено ни одного кадра.");

            var videoDuration = duration > 0 ? duration : frameIndex * frameDuration;
            var tables = tableTracks.Select(track =>
            {
                var boundsResult = BoundingBox.Create(track.Bounds.X / width, track.Bounds.Y / height,
                    track.Bounds.Width / width, track.Bounds.Height / height);
                if (boundsResult.IsFailure) throw new InvalidDataException(boundsResult.Error);
                var tableResult = TableAnalysis.Create(track.Id, boundsResult.Value,
                    TimelineCalculations.BuildSegments(track.Samples, frameDuration, videoDuration));
                if (tableResult.IsFailure) throw new InvalidDataException(tableResult.Error);
                return tableResult.Value;
            }).ToArray();
            var result = VideoAnalysis.Create(videoDuration, tables);
            if (result.IsFailure) throw new InvalidDataException(result.Error);
            return result.Value;
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { logger.LogWarning(exception, "Could not delete temporary video {Path}", temporaryPath); }
        }
    }

    private async Task<(int Width, int Height, double Duration)> ReadMetadata(string path, CancellationToken cancellationToken)
    {
        using var process = Start(_options.FfprobePath, ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height:format=duration", "-of", "json", path]);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        var output = await outputTask;
        if (process.ExitCode != 0) throw new InvalidOperationException($"Не удалось прочитать видео ffprobe: {(await errorTask).Trim()}");
        using var json = JsonDocument.Parse(output);
        var stream = json.RootElement.GetProperty("streams").EnumerateArray().FirstOrDefault();
        if (stream.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("В файле не найдена видеодорожка.");
        var width = stream.GetProperty("width").GetInt32();
        var height = stream.GetProperty("height").GetInt32();
        var duration = json.RootElement.TryGetProperty("format", out var format) &&
                       format.TryGetProperty("duration", out var durationElement) &&
                       double.TryParse(durationElement.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
        return (width, height, duration);
    }

    private Process StartFfmpeg(string path, int width, int height, double? samplingFramesPerSecond = null) => Start(_options.FfmpegPath,
        ["-v", "error", "-i", path, "-vf", $"fps={(samplingFramesPerSecond ?? _options.SamplingFramesPerSecond).ToString(System.Globalization.CultureInfo.InvariantCulture)},scale={width}:{height}", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"]);

    private static Process Start(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try { return Process.Start(start) ?? throw new InvalidOperationException($"Не удалось запустить {executable}."); }
        catch (System.ComponentModel.Win32Exception error) { throw new InvalidOperationException($"Не найдена программа {executable}. Установите FFmpeg и добавьте её в PATH.", error); }
    }

    private static async Task<bool> ReadFrame(Stream stream, byte[] frame, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < frame.Length)
        {
            var read = await stream.ReadAsync(frame.AsMemory(offset), cancellationToken);
            if (read == 0) return offset == frame.Length;
            offset += read;
        }
        return true;
    }

    private sealed class TableTrack(string id, Box bounds, IReadOnlyList<Box> seatingZones)
    {
        public string Id { get; } = id;
        public Box Bounds { get; } = bounds;
        public IReadOnlyList<Box> SeatingZones { get; } = seatingZones;
        public SeatingAnalysis Seating { get; } = new(bounds, seatingZones, releaseGracePeriod: 10.0);
        public List<(double Time, TableState State)> Samples { get; } = [];
    }
}
