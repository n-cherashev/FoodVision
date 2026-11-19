using System.Diagnostics;
using System.Text.Json;
using FoodVision.Api.Domain;
using Microsoft.Extensions.Options;

namespace FoodVision.Api.Infrastructure;

public sealed class VideoAnalyzer(IOptions<AnalysisOptions> configured, ILogger<VideoAnalyzer> logger) : IVideoAnalyzer
{
    private static readonly SemaphoreSlim GpuCapacity = new(1, 1);
    private readonly AnalysisOptions _options = configured.Value;

    public async Task<VideoAnalysis> Analyze(IFormFile video, CancellationToken cancellationToken)
    {
        await GpuCapacity.WaitAsync(cancellationToken);
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"foodvision-{Guid.NewGuid():N}{Path.GetExtension(video.FileName)}");
        try
        {
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await video.CopyToAsync(destination, cancellationToken);

            var (sourceWidth, sourceHeight, duration) = await ReadMetadata(temporaryPath, cancellationToken);
            var ratio = Math.Min((double)_options.FrameSize / sourceWidth, (double)_options.FrameSize / sourceHeight);
            var width = Math.Max(2, (int)(sourceWidth * ratio) / 2 * 2);
            var height = Math.Max(2, (int)(sourceHeight * ratio) / 2 * 2);

            using var tablesModel = new YoloOnnxModel(_options.TableDetectionModelPath, _options.FrameSize, pose: false);
            using var peopleModel = new YoloOnnxModel(_options.PersonPoseModelPath, _options.FrameSize, pose: true);
            using var decoder = StartFfmpeg(temporaryPath, width, height);
            var decoderErrorTask = decoder.StandardError.ReadToEndAsync(cancellationToken);
            var frame = new byte[checked(width * height * 3)];
            var tableTracks = new List<TableTrack>();
            var frameIndex = 0;
            var frameDuration = 1 / Math.Max(.1, _options.SamplingFramesPerSecond);

            try
            {
                while (await ReadFrame(decoder.StandardOutput.BaseStream, frame, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var seconds = frameIndex * frameDuration;
                    var tableDetections = tablesModel.Detect(frame, width, height, .1f, classId: 60);
                    if (tableTracks.Count == 0 && tableDetections.Count > 0)
                    {
                        foreach (var table in tableDetections.OrderBy(detection => detection.Box.Y).ThenBy(detection => detection.Box.X))
                            tableTracks.Add(new TableTrack($"Стол {tableTracks.Count + 1}", table.Box));
                        logger.LogInformation("Detected {Count} tables in the first usable frame at {TimeSeconds:F1} seconds.", tableTracks.Count, seconds);
                    }

                    if (tableTracks.Count > 0)
                    {
                        var people = peopleModel.Detect(frame, width, height, .08f, classId: 0);
                        foreach (var table in tableTracks)
                        {
                            var visible = tableDetections.Any(detection => YoloOnnxModel.IoU(table.Bounds, detection.Box) >= .2f);
                            var state = visible ? Classify(table.Bounds, people) : TableState.Review;
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
                    BuildSegments(track.Samples, frameDuration, videoDuration));
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
            finally { GpuCapacity.Release(); }
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

    private Process StartFfmpeg(string path, int width, int height) => Start(_options.FfmpegPath,
        ["-v", "error", "-i", path, "-vf", $"fps={_options.SamplingFramesPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture)},scale={width}:{height}", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"]);

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

    private static TableState Classify(Box table, IReadOnlyList<Detection> people)
    {
        var zone = new Box(table.X - table.Width * .4f, table.Y - table.Height * .75f, table.Width * 1.8f, table.Height * 2.5f);
        var candidates = people.Where(person => YoloOnnxModel.IoU(person.Box, zone) > .01f ||
            (person.Box.X < zone.X + zone.Width && person.Box.X + person.Box.Width > zone.X &&
             person.Box.Y < zone.Y + zone.Height && person.Box.Y + person.Box.Height > zone.Y)).ToArray();
        if (candidates.Any(person => person.Seated && person.HasHip &&
            person.HipX >= table.X - table.Width * .2f && person.HipX <= table.X + table.Width * 1.2f &&
            person.HipY >= table.Y - table.Height * .35f && person.HipY <= table.Y + table.Height * 1.2f))
            return TableState.Occupied;
        if (candidates.Length > 0) return TableState.Review;
        return TableState.Available;
    }

    private static IReadOnlyList<TimeSegment> BuildSegments(IReadOnlyList<(double Time, TableState State)> samples, double interval, double duration)
    {
        if (samples.Count == 0) return [];
        var smoothed = samples.Select(sample => sample.State).ToArray();
        var runs = new List<(int Start, int Count, TableState State)>();
        for (var index = 0; index < smoothed.Length;)
        {
            var end = index + 1;
            while (end < smoothed.Length && smoothed[end] == smoothed[index]) end++;
            runs.Add((index, end - index, smoothed[index]));
            index = end;
        }
        foreach (var (index, run) in runs.Select((run, index) => (index, run)))
        {
            if (run.Count >= 3 || runs.Count == 1) continue;
            var replacement = index > 0 ? smoothed[run.Start - 1] : smoothed[run.Start + run.Count];
            Array.Fill(smoothed, replacement, run.Start, run.Count);
        }

        var segments = new List<TimeSegment>();
        var start = samples[0].Time;
        var current = smoothed[0];
        for (var i = 1; i < samples.Count; i++)
        {
            if (smoothed[i] == current) continue;
            segments.Add(CreateSegment(start, Math.Min(duration, samples[i].Time), current));
            start = samples[i].Time;
            current = smoothed[i];
        }
        segments.Add(CreateSegment(start, Math.Min(duration, Math.Max(start + interval, samples[^1].Time + interval)), current));
        return segments;
    }

    private static TimeSegment CreateSegment(double start, double end, TableState state)
    {
        var result = TimeSegment.Create(start, end, state);
        if (result.IsFailure) throw new InvalidDataException(result.Error);
        return result.Value;
    }

    private sealed class TableTrack(string id, Box bounds)
    {
        public string Id { get; } = id;
        public Box Bounds { get; } = bounds;
        public List<(double Time, TableState State)> Samples { get; } = [];
    }
}
