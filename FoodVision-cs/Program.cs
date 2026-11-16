using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

const int inputSize = 640;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: dotnet run -- <video-path> <model.onnx> [runs]");
    return 2;
}

var videoPath = Path.GetFullPath(args[0]);
var modelPath = Path.GetFullPath(args[1]);
var runs = args.Length == 3 && int.TryParse(args[2], out var parsedRuns) && parsedRuns > 0 ? parsedRuns : 10;

if (!File.Exists(videoPath) || !File.Exists(modelPath))
{
    Console.Error.WriteLine("Video or model file was not found.");
    return 2;
}

Cv2.SetNumThreads(1);
using var session = new InferenceSession(modelPath, new SessionOptions
{
    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
    IntraOpNumThreads = 1,
    InterOpNumThreads = 1,
});

var inputName = session.InputMetadata.Keys.Single();
Console.WriteLine($"Video: {videoPath}");
Console.WriteLine($"Model: {modelPath}");
Console.WriteLine($"Input: {inputName}; runs: {runs}; provider: CPU");

var durations = new List<double>(runs);
var frameCounts = new List<int>(runs);

for (var run = 1; run <= runs; run++)
{
    var stopwatch = Stopwatch.StartNew();
    var frames = 0;

    using var capture = new VideoCapture(videoPath);
    if (!capture.IsOpened())
    {
        Console.Error.WriteLine("OpenCV could not open the video.");
        return 3;
    }

    using var frame = new Mat();
    using var resized = new Mat();
    // The supplied YOLOv5n artifact has a float16 input. Keep preprocessing in
    // the same precision that is passed to ONNX Runtime.
    var input = new DenseTensor<Float16>(new[] { 1, 3, inputSize, inputSize });

    while (capture.Read(frame) && !frame.Empty())
    {
        Cv2.Resize(frame, resized, new Size(inputSize, inputSize), 0, 0, InterpolationFlags.Linear);
        CopyBgrToRgbChw(resized, input.Buffer.Span);

        var inputValue = NamedOnnxValue.CreateFromTensor(inputName, input);
        using var results = session.Run(new[] { inputValue });
        frames++;
    }

    stopwatch.Stop();
    durations.Add(stopwatch.Elapsed.TotalSeconds);
    frameCounts.Add(frames);
    Console.WriteLine($"Run {run,3}: {frames} frames, {stopwatch.Elapsed.TotalSeconds:F3} s, {frames / stopwatch.Elapsed.TotalSeconds:F2} FPS");
}

var totalFrames = frameCounts.Sum();
var totalSeconds = durations.Sum();
Console.WriteLine();
Console.WriteLine($"Total: {runs} runs, {totalFrames} frames, {totalSeconds:F3} s, {totalFrames / totalSeconds:F2} FPS");
Console.WriteLine($"Per-run: mean {durations.Average():F3} s; min {durations.Min():F3} s; max {durations.Max():F3} s");

return 0;

static unsafe void CopyBgrToRgbChw(Mat source, Span<Float16> destination)
{
    if (source.Type() != MatType.CV_8UC3 || !source.IsContinuous())
    {
        throw new InvalidOperationException("Expected a contiguous 8-bit BGR image.");
    }

    var pixels = (byte*)source.DataPointer;
    var plane = inputSize * inputSize;

    for (var y = 0; y < inputSize; y++)
    {
        var row = pixels + (y * source.Step());
        for (var x = 0; x < inputSize; x++)
        {
            var sourceIndex = x * 3;
            var targetIndex = (y * inputSize) + x;
            destination[targetIndex] = (Float16)(row[sourceIndex + 2] / 255f);
            destination[plane + targetIndex] = (Float16)(row[sourceIndex + 1] / 255f);
            destination[(2 * plane) + targetIndex] = (Float16)(row[sourceIndex] / 255f);
        }
    }
}
