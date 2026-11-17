using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

const int inputSize = 640;

if (args.Length is < 2 or > 4)
{
    Console.Error.WriteLine("Usage: dotnet run -- <video-path> <model.onnx> [rounds] [cameras]");
    return 2;
}

var videoPath = Path.GetFullPath(args[0]);
var modelPath = Path.GetFullPath(args[1]);
var roundCount = args.Length >= 3 ? ParsePositiveInteger(args[2], "rounds") : 10;
var cameraCount = args.Length == 4 ? ParsePositiveInteger(args[3], "cameras") : 1;

if (!File.Exists(videoPath) || !File.Exists(modelPath))
{
    Console.Error.WriteLine("Video or model file was not found.");
    return 2;
}

Cv2.SetNumThreads(1);
var cameras = Enumerable.Range(1, cameraCount)
    .Select(cameraId => new CameraContext(cameraId, modelPath))
    .ToArray();

try
{
    var startGates = Enumerable.Range(0, roundCount)
        .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
        .ToArray();
    var completionGates = Enumerable.Range(0, roundCount)
        .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
        .ToArray();
    var readyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var readyCameras = 0;
    var completedCameras = new int[roundCount];
    var framesByRound = new int[roundCount];
    var tasks = cameras
        .Select(camera => Task.Run(async () =>
        {
            if (Interlocked.Increment(ref readyCameras) == cameraCount)
            {
                readyGate.SetResult();
            }

            return await ProcessCameraAsync(
                    camera,
                    videoPath,
                    startGates,
                    completionGates,
                    completedCameras,
                    framesByRound,
                    cameraCount)
                .ConfigureAwait(false);
        }))
        .ToArray();

    Console.WriteLine($"Video: {videoPath}");
    Console.WriteLine($"Model: {modelPath}");
    Console.WriteLine($"Virtual cameras: {cameraCount}; synchronized rounds: {roundCount}; provider: CPU");
    Console.WriteLine($"Input: {cameras[0].InputName}; cv_threads=1; ort_intra=1; ort_inter=1");

    await readyGate.Task.ConfigureAwait(false);
    var roundResults = new List<RoundResult>(roundCount);
    for (var round = 0; round < roundCount; round++)
    {
        var stopwatch = Stopwatch.StartNew();
        startGates[round].SetResult();
        await completionGates[round].Task.ConfigureAwait(false);
        stopwatch.Stop();

        roundResults.Add(new RoundResult(round + 1, framesByRound[round], stopwatch.Elapsed.TotalSeconds));
    }

    var cameraResults = await Task.WhenAll(tasks);
    var expectedFrames = cameraResults[0].FramesPerRound;
    if (cameraResults.Any(result => result.FramesPerRound != expectedFrames))
    {
        throw new InvalidOperationException("Virtual cameras processed different numbers of frames.");
    }

    foreach (var result in roundResults)
    {
        Console.WriteLine(
            $"Round {result.Round,2}: {result.Frames} frames, {result.Seconds:F3} s, {result.AggregateFps:F2} aggregate FPS");
    }

    var aggregateFps = roundResults.Select(result => result.AggregateFps).ToArray();
    Console.WriteLine();
    Console.WriteLine(
        $"Summary: cameras={cameraCount}, rounds={roundCount}, frames_per_camera={expectedFrames}, " +
        $"total_frames={roundResults.Sum(result => result.Frames)}");
    Console.WriteLine(
        $"Aggregate FPS: mean {aggregateFps.Average():F2}; min {aggregateFps.Min():F2}; max {aggregateFps.Max():F2}");
}
finally
{
    foreach (var camera in cameras)
    {
        camera.Dispose();
    }
}

return 0;

static async Task<CameraResult> ProcessCameraAsync(
    CameraContext camera,
    string videoPath,
    IReadOnlyList<TaskCompletionSource> startGates,
    IReadOnlyList<TaskCompletionSource> completionGates,
    int[] completedCameras,
    int[] framesByRound,
    int cameraCount)
{
    int? framesPerRound = null;

    for (var round = 0; round < startGates.Count; round++)
    {
        await startGates[round].Task.ConfigureAwait(false);
        var frames = ProcessVideoPass(camera, videoPath);
        if (framesPerRound is not null && frames != framesPerRound)
        {
            throw new InvalidOperationException(
                $"Camera {camera.Id} read {frames} frames in round {round + 1}; expected {framesPerRound}.");
        }

        framesPerRound = frames;
        Interlocked.Add(ref framesByRound[round], frames);
        if (Interlocked.Increment(ref completedCameras[round]) == cameraCount)
        {
            completionGates[round].SetResult();
        }
    }

    return new CameraResult(camera.Id, framesPerRound ?? 0);
}

static int ProcessVideoPass(CameraContext camera, string videoPath)
{
    using var capture = new VideoCapture(videoPath);
    if (!capture.IsOpened())
    {
        throw new InvalidOperationException($"Camera {camera.Id} could not open the video.");
    }

    using var frame = new Mat();
    using var resized = new Mat();
    using var rgb = new Mat();
    using var normalized = new Mat();
    var input = new DenseTensor<Float16>(new[] { 1, 3, inputSize, inputSize });
    var frames = 0;

    while (capture.Read(frame) && !frame.Empty())
    {
        Cv2.Resize(frame, resized, new Size(inputSize, inputSize), 0, 0, InterpolationFlags.Linear);
        Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);
        rgb.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);
        CopyRgbHwcToChw(normalized, input.Buffer.Span);

        var inputValue = NamedOnnxValue.CreateFromTensor(camera.InputName, input);
        using var results = camera.Session.Run(new[] { inputValue });
        frames++;
    }

    return frames;
}

static int ParsePositiveInteger(string value, string name) =>
    int.TryParse(value, out var parsed) && parsed > 0
        ? parsed
        : throw new ArgumentOutOfRangeException(name, $"{name} must be a positive integer.");

static unsafe void CopyRgbHwcToChw(Mat source, Span<Float16> destination)
{
    if (source.Type() != MatType.CV_32FC3 || !source.IsContinuous())
    {
        throw new InvalidOperationException("Expected a contiguous float32 RGB image.");
    }

    var pixels = (float*)source.DataPointer;
    var plane = inputSize * inputSize;

    for (var y = 0; y < inputSize; y++)
    {
        var row = (float*)((byte*)pixels + (y * source.Step()));
        for (var x = 0; x < inputSize; x++)
        {
            var sourceIndex = x * 3;
            var targetIndex = (y * inputSize) + x;
            destination[targetIndex] = (Float16)row[sourceIndex];
            destination[plane + targetIndex] = (Float16)row[sourceIndex + 1];
            destination[(2 * plane) + targetIndex] = (Float16)row[sourceIndex + 2];
        }
    }
}

sealed class CameraContext : IDisposable
{
    public CameraContext(int id, string modelPath)
    {
        Id = id;
        Session = new InferenceSession(modelPath, new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
        });
        InputName = Session.InputMetadata.Keys.Single();
    }

    public int Id { get; }

    public InferenceSession Session { get; }

    public string InputName { get; }

    public void Dispose() => Session.Dispose();
}

sealed record CameraResult(int CameraId, int FramesPerRound);

sealed record RoundResult(int Round, int Frames, double Seconds)
{
    public double AggregateFps => Frames / Seconds;
}
