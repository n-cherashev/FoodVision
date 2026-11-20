using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FoodVision.Api.Domain;
using FoodVision.Api.Infrastructure;
using FoodVision.Api.Utilites;
using Microsoft.Extensions.Options;

namespace FoodVision.Api.Benchmark;

internal sealed class BenchmarkRunner(IOptions<AnalysisOptions> configured, ILogger<BenchmarkRunner> logger, InferenceRuntime inferenceRuntime)
{
    private const int StreamCount = 50;
    private const int DeliveryFps = 5;
    private static readonly SemaphoreSlim SocketSend = new(1, 1);
    private static readonly SemaphoreSlim ActiveBenchmark = new(1, 1);
    private readonly AnalysisOptions _options = configured.Value;

    public async Task Run(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var view = new ViewRange();
        await Send(socket, new { type = "ready", streams = StreamCount, file = "videos/Sample_Video_1.mp4", analyticsFps = _options.SamplingFramesPerSecond }, lifetime.Token);
        var buffer = new byte[1024];
        CancellationTokenSource? runLifetime = null;
        Task? runTask = null;
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var received = await socket.ReceiveAsync(buffer, lifetime.Token);
                if (received.MessageType == WebSocketMessageType.Close) break;
                var command = Encoding.UTF8.GetString(buffer, 0, received.Count);
                var action = ReadAction(command);
                if (action == "view")
                {
                    view.Update(command, StreamCount);
                }
                else if (action == "start" && (runTask is null || runTask.IsCompleted))
                {
                    runLifetime?.Dispose();
                    runLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    runTask = RunStreams(socket, view, runLifetime.Token);
                    _ = runTask.ContinueWith(async _ =>
                    {
                        if (socket.State == WebSocketState.Open) await Send(socket, new { type = "run-complete" }, lifetime.Token);
                    }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
                }
                else if (action == "stop") runLifetime?.Cancel();
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            lifetime.Cancel();
            runLifetime?.Cancel();
            if (runTask is not null) try { await runTask; } catch (OperationCanceledException) { }
            runLifetime?.Dispose();
            if (socket.State == WebSocketState.Open) await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Benchmark stopped", CancellationToken.None);
        }
    }

    private async Task RunStreams(WebSocket socket, ViewRange view, CancellationToken cancellationToken)
    {
        if (!await ActiveBenchmark.WaitAsync(0, cancellationToken))
        {
            await Send(socket, new { type = "fatal", error = "Другой 50-поточный бенчмарк уже выполняется." }, cancellationToken);
            return;
        }

        try { await RunStreamsCore(socket, view, cancellationToken); }
        finally { ActiveBenchmark.Release(); }
    }

    private async Task RunStreamsCore(WebSocket socket, ViewRange view, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "videos", "Sample_Video_1.mp4");
        if (!File.Exists(path)) { await Send(socket, new { type = "fatal", error = $"Не найден файл {path}. Подготовьте видео по инструкции README." }, cancellationToken); return; }
        try
        {
            await Send(socket, new { type = "preparing" }, cancellationToken);
            var videoInfo = await ReadVideoInfo(path, cancellationToken);
            var detector = inferenceRuntime.TableDetector;
            var pose = inferenceRuntime.PersonPose;
            var personDetector = inferenceRuntime.PersonDetector;
            // Model initialization alone does not execute CUDA kernels; keep warm-up
            // outside the measured run and do not start decoders until it finishes.
            using (await GpuInferenceGate.EnterInferenceAsync(cancellationToken))
            {
                using var inputLease = inferenceRuntime.RentInput();
                await Task.Run(() =>
                {
                    var frame = new byte[640 * 360 * 3];
                    var workspace = inputLease.Workspace;
                    workspace.Prepare(frame, 640, 360);
                    detector.Detect(workspace.TableInput, .1f, -1);
                    cancellationToken.ThrowIfCancellationRequested();
                    var poses = pose.Detect(workspace.PoseInput, .08f, 0);
                    cancellationToken.ThrowIfCancellationRequested();
                    personDetector.Detect(workspace.PersonInput, poses);
                    cancellationToken.ThrowIfCancellationRequested();
                }, cancellationToken);
            }
            var started = Stopwatch.GetTimestamp();
            var startedAt = DateTimeOffset.UtcNow;
            await Send(socket, new { type = "run-start", startedAt, appVersion = typeof(BenchmarkRunner).Assembly.GetName().Version?.ToString() ?? "unknown", streams = StreamCount, sourceFps = videoInfo.Fps, durationSeconds = videoInfo.Duration, sourceResolution = $"{videoInfo.Width}x{videoInfo.Height}", videoFps = DeliveryFps, analyticsFps = _options.SamplingFramesPerSecond, gpuConcurrency = GpuInferenceGate.BenchmarkConcurrency, resolution = "640x360", model = Path.GetFileName(_options.PersonDetectionModelPath) + " + " + Path.GetFileName(_options.PersonPoseModelPath) + " + " + Path.GetFileName(_options.TableDetectionModelPath), cpu = "process CPU", memory = "process working set", gpu = "unavailable: NVIDIA telemetry tool is not installed", videoMemory = "unavailable: NVIDIA telemetry tool is not installed" }, cancellationToken);
            var tasks = Enumerable.Range(1, StreamCount).Select(id => ProcessStream(socket, id, path, detector, pose, personDetector, view, started, cancellationToken)).ToArray();
            await Task.WhenAll(tasks);
            await Send(socket, new { type = "run-end", endedAt = DateTimeOffset.UtcNow, elapsedSeconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency }, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "Benchmark could not start");
            await Send(socket, new { type = "fatal", error = error.Message }, cancellationToken);
        }
    }

    private async Task ProcessStream(WebSocket socket, int id, string path, YoloOnnxModel detector, YoloOnnxModel pose, PersonDetector personDetector, ViewRange view, long runStarted, CancellationToken token)
    {
        var start = DateTimeOffset.UtcNow;
        var process = new Process { StartInfo = new ProcessStartInfo(_options.FfmpegPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        var processStarted = false;
        foreach (var arg in new[] { "-v", "error", "-threads", "1", "-filter_threads", "1", "-re", "-i", path, "-vf", $"fps={DeliveryFps},scale=640:360", "-f", "rawvideo", "-pix_fmt", "bgr24", "-" }) process.StartInfo.ArgumentList.Add(arg);
        long accepted = 0, processed = 0, skipped = 0, overBudget = 0, delivered = 0, hidden = 0, dropped = 0;
        long lastMetricAt = 0;
        var wasSubscribed = false;
        var subscribedAt = 0L;
        var personDetections = 0;
        var objectDetections = 0;
        var proposer = new TableLayoutProposer(640, 360);
        var tables = new List<BenchmarkTable>();
        IReadOnlyDictionary<string, int> tableStates = new Dictionary<string, int>();
        var latencies = new RollingSamples();
        var queueWaits = new RollingSamples();
        var inferenceTimes = new RollingSamples();
        var preprocessingTimes = new RollingSamples();
        var tableInferenceTimes = new RollingSamples();
        var poseInferenceTimes = new RollingSamples();
        var personInferenceTimes = new RollingSamples();
        var statisticsSync = new object();
        var analyticsFrames = new LatestFrameQueue();
        Task? analyticsTask = null;
        async Task AnalyzeFrames()
        {
            while (await analyticsFrames.Take(token) is { } analyticsFrame)
            {
                try
                {
                    var due = runStarted + (long)(analyticsFrame.Index * Stopwatch.Frequency / DeliveryFps);
                    var queueStart = Stopwatch.GetTimestamp();
                    using var gpuLease = await GpuInferenceGate.EnterInferenceAsync(token);
                    using var inputLease = inferenceRuntime.RentInput();
                    IReadOnlyList<Detection> detections;
                    IReadOnlyList<Detection> people;
                    var inferenceStart = Stopwatch.GetTimestamp();
                    var workspace = inputLease.Workspace;
                    var stageStart = Stopwatch.GetTimestamp();
                    workspace.Prepare(analyticsFrame.Frame, 640, 360);
                    var preprocessingMs = (Stopwatch.GetTimestamp() - stageStart) * 1000d / Stopwatch.Frequency;
                    stageStart = Stopwatch.GetTimestamp();
                    detections = detector.Detect(workspace.TableInput, .1f, -1);
                    var tableInferenceMs = (Stopwatch.GetTimestamp() - stageStart) * 1000d / Stopwatch.Frequency;
                    stageStart = Stopwatch.GetTimestamp();
                    var poses = pose.Detect(workspace.PoseInput, .08f, 0);
                    var poseInferenceMs = (Stopwatch.GetTimestamp() - stageStart) * 1000d / Stopwatch.Frequency;
                    stageStart = Stopwatch.GetTimestamp();
                    people = personDetector.Detect(workspace.PersonInput, poses);
                    var personInferenceMs = (Stopwatch.GetTimestamp() - stageStart) * 1000d / Stopwatch.Frequency;
                    lock (statisticsSync)
                    {
                        objectDetections = detections.Count;
                        personDetections = people.Count;
                        if (tables.Count == 0)
                        {
                            proposer.AddFrame(detections);
                            var proposed = proposer.Build();
                            if (proposed.Count > 0) tables = proposed.Select(table => new BenchmarkTable(table)).ToList();
                        }
                        if (tables.Count > 0)
                        {
                            var assignments = TablePersonAssignment.Assign(people, tables.Select(table => new TableAssociation(table.Bounds, table.SeatingZones)).ToArray());
                            var tableDetections = detections.Where(detection => detection.ClassId == 60).ToArray();
                            var belongings = detections.Where(detection => detection.ClassId is 24 or 26 or 28).ToArray();
                            var counts = new Dictionary<string, int>();
                            for (var index = 0; index < tables.Count; index++)
                            {
                                var table = tables[index];
                                var visible = tableDetections.Any(detection => Geometry.IoU(table.Bounds, detection.Box) >= .2f);
                                var state = table.Seating.Classify(analyticsFrame.Index / (double)DeliveryFps, assignments.People[index], belongings, visible, assignments.Ambiguous[index]);
                                var stateName = state == TableState.OccupiedWithItems ? "occupied_with_items" : state.ToString().ToLowerInvariant();
                                counts[stateName] = counts.GetValueOrDefault(stateName) + 1;
                            }
                            tableStates = counts;
                        }
                    }
                    var inferenceEnd = Stopwatch.GetTimestamp();
                    lock (statisticsSync)
                    {
                        queueWaits.Add((inferenceStart - queueStart) * 1000d / Stopwatch.Frequency);
                        inferenceTimes.Add((inferenceEnd - inferenceStart) * 1000d / Stopwatch.Frequency);
                        preprocessingTimes.Add(preprocessingMs);
                        tableInferenceTimes.Add(tableInferenceMs);
                        poseInferenceTimes.Add(poseInferenceMs);
                        personInferenceTimes.Add(personInferenceMs);
                        processed++;
                        var delayMs = Math.Max(0, (Stopwatch.GetTimestamp() - due) * 1000d / Stopwatch.Frequency);
                        latencies.Add(delayMs);
                        if (delayMs > 1000 / Math.Max(.1, _options.SamplingFramesPerSecond)) overBudget++;
                    }
                }
                finally { ArrayPool<byte>.Shared.Return(analyticsFrame.Frame); }
            }
        }
        AnalyticsSnapshot Snapshot()
        {
            lock (statisticsSync)
            {
                var queue = queueWaits.Summarize();
                var inference = inferenceTimes.Summarize();
                var latency = latencies.Summarize();
                var preprocessing = preprocessingTimes.Summarize();
                var tableInference = tableInferenceTimes.Summarize();
                var poseInference = poseInferenceTimes.Summarize();
                var personInference = personInferenceTimes.Summarize();
                return new AnalyticsSnapshot(processed, overBudget, tables.Count, new Dictionary<string, int>(tableStates), objectDetections, personDetections,
                    queue.Average, queue.P95, queue.Maximum, inference.Average, inference.P95, inference.Maximum,
                    preprocessing.Average, tableInference.Average, poseInference.Average, personInference.Average,
                    latency.Average, latency.P95, latency.Maximum);
            }
        }
        try
        {
            if (!process.Start()) throw new InvalidOperationException("FFmpeg не запустился.");
            processStarted = true;
            var stderr = process.StandardError.ReadToEndAsync(token);
            analyticsTask = AnalyzeFrames();
            var frame = new byte[640 * 360 * 3];
            while (await ReadFrame(process.StandardOutput.BaseStream, frame, token))
            {
                accepted++;
                var analyticsStep = Math.Max(1, (int)Math.Round(DeliveryFps / Math.Max(.1, _options.SamplingFramesPerSecond)));
                var analyzeThisFrame = (accepted - 1) % analyticsStep == 0;
                if (!analyzeThisFrame) skipped++;
                if (analyzeThisFrame)
                {
                    if (analyticsFrames.Replace(accepted - 1, frame)) dropped++;
                }
                var subscribed = view.Contains(id);
                if (subscribed && !wasSubscribed) { subscribedAt = Stopwatch.GetTimestamp(); delivered = 0; }
                wasSubscribed = subscribed;
                if (subscribed)
                {
                    const int headerLength = 14;
                    var packetLength = headerLength + 640 * 360 * 4;
                    var packet = ArrayPool<byte>.Shared.Rent(packetLength);
                    try
                    {
                        packet[0] = 1; packet[1] = (byte)id;
                        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), 640);
                        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), 360);
                        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(6), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                        var target = headerLength;
                        for (var i = 0; i < frame.Length; i += 3) { packet[target++] = frame[i + 2]; packet[target++] = frame[i + 1]; packet[target++] = frame[i]; packet[target++] = 255; }
                        await SendBinary(socket, packet.AsMemory(0, packetLength), token);
                    }
                    finally { ArrayPool<byte>.Shared.Return(packet); }
                    delivered++;
                }
                else hidden++;
                var now = Stopwatch.GetTimestamp();
                if (accepted == 1 || now - lastMetricAt >= Stopwatch.Frequency)
                {
                    lastMetricAt = now;
                    var stats = Snapshot();
                    var elapsed = (now - runStarted) / (double)Stopwatch.Frequency;
                    using var current = Process.GetCurrentProcess();
                    var cpuPercent = current.TotalProcessorTime.TotalMilliseconds / Math.Max(elapsed * 1000 * Environment.ProcessorCount, 1) * 100;
                    await Send(socket, new { type = "stream", id, status = "running", inputFps = DeliveryFps, subscribed, deliveredFps = subscribed ? delivered / Math.Max((now - subscribedAt) / (double)Stopwatch.Frequency, .001) : (double?)null, hiddenFrames = hidden, analyticsFps = stats.Processed / Math.Max(elapsed, .001), accepted, processed = stats.Processed, skipped, dropped, overBudget = stats.OverBudget, tableCount = stats.TableCount, tableStates = stats.TableStates, stats.ObjectDetections, stats.PersonDetections, cpuPercent, memoryMb = current.WorkingSet64 / 1024d / 1024d, stats.QueueWaitAvgMs, stats.QueueWaitP95Ms, stats.QueueWaitMaxMs, stats.InferenceAvgMs, stats.InferenceP95Ms, stats.InferenceMaxMs, stats.PreprocessingAvgMs, stats.TableInferenceAvgMs, stats.PoseInferenceAvgMs, stats.PersonInferenceAvgMs, stats.LatencyAvgMs, stats.LatencyP95Ms, stats.LatencyMaxMs, clientLatencyMs = (double?)null, startedAt = start, error = (string?)null }, token);
                }
            }
            analyticsFrames.Complete();
            await analyticsTask;
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0) throw new InvalidOperationException((await stderr).Trim());
            var completed = Snapshot();
            await Send(socket, new { type = "stream", id, status = "completed", inputFps = DeliveryFps, deliveredFps = wasSubscribed ? delivered / Math.Max((Stopwatch.GetTimestamp() - subscribedAt) / (double)Stopwatch.Frequency, .001) : (double?)null, analyticsFps = completed.Processed / Math.Max((DateTimeOffset.UtcNow - start).TotalSeconds, .001), accepted, processed = completed.Processed, skipped, dropped, overBudget = completed.OverBudget, tableCount = completed.TableCount, tableStates = completed.TableStates, completed.QueueWaitAvgMs, completed.QueueWaitP95Ms, completed.QueueWaitMaxMs, completed.InferenceAvgMs, completed.InferenceP95Ms, completed.InferenceMaxMs, completed.PreprocessingAvgMs, completed.TableInferenceAvgMs, completed.PoseInferenceAvgMs, completed.PersonInferenceAvgMs, completed.LatencyAvgMs, completed.LatencyP95Ms, completed.LatencyMaxMs, clientLatencyMs = (double?)null, startedAt = start, error = (string?)null }, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            var failed = Snapshot();
            await Send(socket, new { type = "stream", id, status = "error", accepted, processed = failed.Processed, skipped, dropped, error = error.Message }, token);
        }
        finally
        {
            analyticsFrames.Complete();
            if (analyticsTask is not null) try { await analyticsTask; } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            if (processStarted && !process.HasExited)
            {
                try { process.Kill(true); }
                catch (InvalidOperationException) { }
            }
            process.Dispose();
        }
    }

    private static async Task<bool> ReadFrame(Stream stream, byte[] frame, CancellationToken token)
    {
        var offset = 0;
        while (offset < frame.Length) { var read = await stream.ReadAsync(frame.AsMemory(offset), token); if (read == 0) return offset == frame.Length; offset += read; }
        return true;
    }
    private async Task<(int Width, int Height, double Fps, double Duration)> ReadVideoInfo(string path, CancellationToken token)
    {
        var start = new ProcessStartInfo(_options.FfprobePath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,avg_frame_rate:format=duration", "-of", "json", path }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить ffprobe.");
        var outputTask = process.StandardOutput.ReadToEndAsync(token);
        var errorTask = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException($"ffprobe завершился с ошибкой: {error.Trim()}");
        using var json = JsonDocument.Parse(output);
        var stream = json.RootElement.GetProperty("streams")[0];
        var rateParts = stream.GetProperty("avg_frame_rate").GetString()!.Split('/');
        var numerator = double.Parse(rateParts[0], System.Globalization.CultureInfo.InvariantCulture);
        var denominator = double.Parse(rateParts[1], System.Globalization.CultureInfo.InvariantCulture);
        if (denominator <= 0) throw new InvalidDataException("ffprobe вернул некорректную частоту кадров видео.");
        var fps = numerator / denominator;
        var duration = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        return (stream.GetProperty("width").GetInt32(), stream.GetProperty("height").GetInt32(), fps, duration);
    }
    private sealed class RollingSamples
    {
        private const int Capacity = 300;
        private readonly double[] _samples = new double[Capacity];
        private int _count;
        private int _next;

        public void Add(double value)
        {
            _samples[_next] = value;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        public (double Average, double P95, double Maximum) Summarize()
        {
            if (_count == 0) return (0, 0, 0);
            var sorted = new double[_count];
            Array.Copy(_samples, sorted, _count);
            Array.Sort(sorted);
            var average = 0d;
            for (var i = 0; i < _count; i++) average += _samples[i];
            average /= _count;
            var p95Index = Math.Clamp((int)Math.Ceiling(.95 * _count) - 1, 0, _count - 1);
            return (average, sorted[p95Index], sorted[^1]);
        }
    }
    private sealed class BenchmarkTable(TableConfiguration table)
    {
        public string Id { get; } = table.Id;
        public Box Bounds { get; } = ToBox(table.Bounds);
        public IReadOnlyList<Box> SeatingZones { get; } = table.SeatingZones.Select(ToBox).ToArray();
        public SeatingAnalysis Seating { get; } = new(ToBox(table.Bounds), table.SeatingZones.Select(ToBox).ToArray());
        private static Box ToBox(NormalizedBounds bounds) => new((float)(bounds.X * 640), (float)(bounds.Y * 360), (float)(bounds.Width * 640), (float)(bounds.Height * 360));
    }

    private static string? ReadAction(string command)
    {
        try
        {
            using var json = JsonDocument.Parse(command);
            return json.RootElement.TryGetProperty("action", out var action) ? action.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private sealed class ViewRange
    {
        private readonly object _sync = new();
        private int _start = 1;
        private int _count = 10;

        public bool Contains(int streamId)
        {
            lock (_sync) return streamId >= _start && streamId < _start + _count;
        }

        public void Update(string command, int streamCount)
        {
            try
            {
                using var json = JsonDocument.Parse(command);
                var root = json.RootElement;
                var start = root.TryGetProperty("start", out var startValue) && startValue.TryGetInt32(out var requestedStart) ? requestedStart : 1;
                var count = root.TryGetProperty("count", out var countValue) && countValue.TryGetInt32(out var requestedCount) ? requestedCount : 10;
                lock (_sync)
                {
                    _start = Math.Clamp(start, 1, streamCount);
                    _count = Math.Clamp(count, 1, streamCount - _start + 1);
                }
            }
            catch (JsonException) { }
        }
    }

    private sealed class LatestFrameQueue
    {
        private readonly object _sync = new();
        private readonly SemaphoreSlim _available = new(0, 2);
        private QueuedFrame? _frame;
        private bool _completed;

        public bool Replace(long index, byte[] frame)
        {
            var copy = ArrayPool<byte>.Shared.Rent(frame.Length);
            frame.AsSpan(0, frame.Length).CopyTo(copy);
            QueuedFrame? replacedFrame;
            lock (_sync)
            {
                if (_completed)
                {
                    ArrayPool<byte>.Shared.Return(copy);
                    return false;
                }
                replacedFrame = _frame;
                _frame = new QueuedFrame(index, copy);
                if (replacedFrame is null) _available.Release();
            }
            if (replacedFrame is not null) ArrayPool<byte>.Shared.Return(replacedFrame.Frame);
            return replacedFrame is not null;
        }

        public async Task<QueuedFrame?> Take(CancellationToken token)
        {
            while (true)
            {
                await _available.WaitAsync(token);
                lock (_sync)
                {
                    if (_frame is { } frame) { _frame = null; return frame; }
                    if (_completed) return null;
                }
                await _available.WaitAsync(token);
            }
        }

        public void Complete()
        {
            lock (_sync)
            {
                if (_completed) return;
                _completed = true;
                _available.Release();
            }
        }
    }

    private sealed record QueuedFrame(long Index, byte[] Frame);
    private sealed record AnalyticsSnapshot(long Processed, long OverBudget, int TableCount, IReadOnlyDictionary<string, int> TableStates, int ObjectDetections, int PersonDetections,
        double QueueWaitAvgMs, double QueueWaitP95Ms, double QueueWaitMaxMs, double InferenceAvgMs, double InferenceP95Ms, double InferenceMaxMs,
        double PreprocessingAvgMs, double TableInferenceAvgMs, double PoseInferenceAvgMs, double PersonInferenceAvgMs,
        double LatencyAvgMs, double LatencyP95Ms, double LatencyMaxMs);

    private static async Task Send(WebSocket socket, object value, CancellationToken token)
    {
        await SocketSend.WaitAsync(token);
        try { await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))), WebSocketMessageType.Text, true, token); }
        finally { SocketSend.Release(); }
    }
    private static async Task SendBinary(WebSocket socket, ReadOnlyMemory<byte> value, CancellationToken token)
    {
        await SocketSend.WaitAsync(token);
        try { await socket.SendAsync(value, WebSocketMessageType.Binary, true, token); }
        finally { SocketSend.Release(); }
    }
}
