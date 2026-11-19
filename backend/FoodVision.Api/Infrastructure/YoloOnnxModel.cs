using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using FoodVision.Api.Utilites;

namespace FoodVision.Api.Infrastructure;

internal sealed class YoloOnnxModel : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly int _inputWidth;
    private readonly int _inputHeight;
    private readonly bool _pose;

    public YoloOnnxModel(string path, int size, bool pose)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Не найдена модель: {path}. Укажите путь через Analysis:{(pose ? "PersonPoseModelPath" : "TableDetectionModelPath")}.", path);
        try
        {
            using var options = Microsoft.ML.OnnxRuntime.SessionOptions.MakeSessionOptionWithCudaProvider(0);
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
            _session = new InferenceSession(path, options);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Не удалось запустить модель {Path.GetFileName(path)} на NVIDIA GPU. Проверьте драйвер NVIDIA и совместимые CUDA/cuDNN runtime-библиотеки. Переключение на CPU отключено. {error.Message}", error);
        }
        _inputName = _session.InputMetadata.Keys.First();
        var dimensions = _session.InputMetadata[_inputName].Dimensions;
        _inputHeight = dimensions[^2] > 0 ? dimensions[^2] : size;
        _inputWidth = dimensions[^1] > 0 ? dimensions[^1] : size;
        _pose = pose;
    }

    public IReadOnlyList<Detection> Detect(byte[] bgr, int width, int height, float threshold, int classId)
    {
        var input = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });
        for (var i = 0; i < input.Length; i++) input.Buffer.Span[i] = 114f / 255f;
        var scale = Math.Min((float)_inputWidth / width, (float)_inputHeight / height);
        var scaledWidth = (int)Math.Round(width * scale);
        var scaledHeight = (int)Math.Round(height * scale);
        var padX = (_inputWidth - scaledWidth) / 2;
        var padY = (_inputHeight - scaledHeight) / 2;

        for (var y = 0; y < scaledHeight; y++)
        {
            var sourceY = Math.Min(height - 1, (int)(y / scale));
            for (var x = 0; x < scaledWidth; x++)
            {
                var sourceX = Math.Min(width - 1, (int)(x / scale));
                var offset = (sourceY * width + sourceX) * 3;
                input[0, 0, y + padY, x + padX] = bgr[offset + 2] / 255f;
                input[0, 1, y + padY, x + padX] = bgr[offset + 1] / 255f;
                input[0, 2, y + padY, x + padX] = bgr[offset] / 255f;
            }
        }

        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, input)]);
        var output = results.First().AsTensor<float>();
        var shape = output.Dimensions.ToArray();
        var transposed = shape.Length == 3 && shape[1] < shape[2];
        var attributes = shape[transposed ? 1 : 2];
        var candidates = shape[transposed ? 2 : 1];
        float At(int candidate, int attribute) => transposed ? output[0, attribute, candidate] : output[0, candidate, attribute];
        var classCount = _pose ? 1 : attributes - 4;
        if (classCount < 1) throw new InvalidDataException($"Неизвестный формат выхода YOLO: [{string.Join(',', shape)}].");

        var detections = new List<Detection>();
        for (var i = 0; i < candidates; i++)
        {
            var predictedClass = 0;
            var confidence = At(i, 4);
            if (!_pose)
            {
                confidence = 0;
                for (var c = 0; c < classCount; c++)
                {
                    var score = At(i, c + 4);
                    if (score > confidence) { confidence = score; predictedClass = c; }
                }
            }
            if ((classId >= 0 && predictedClass != classId) || confidence < threshold) continue;

            var centerX = (At(i, 0) - padX) / scale;
            var centerY = (At(i, 1) - padY) / scale;
            var boxWidth = At(i, 2) / scale;
            var boxHeight = At(i, 3) / scale;
            var left = Math.Clamp(centerX - boxWidth / 2, 0, width);
            var top = Math.Clamp(centerY - boxHeight / 2, 0, height);
            var right = Math.Clamp(centerX + boxWidth / 2, 0, width);
            var bottom = Math.Clamp(centerY + boxHeight / 2, 0, height);
            if (right - left < 2 || bottom - top < 2) continue;

            float hipY = 0, hipX = 0, hipWeight = 0;
            var keypoints = new (float X, float Y, float Confidence)[17];
            if (_pose)
            {
                for (var joint = 0; joint < 17; joint++)
                {
                    var x = (At(i, 5 + joint * 3) - padX) / scale;
                    var y = (At(i, 6 + joint * 3) - padY) / scale;
                    var confidencePoint = At(i, 7 + joint * 3);
                    keypoints[joint] = (x, y, confidencePoint);
                    if (joint is 11 or 12 && confidencePoint > .2f) { hipX += x * confidencePoint; hipY += y * confidencePoint; hipWeight += confidencePoint; }
                }
            }
            var seated = _pose && (PoseCalculations.HasFlexedKnee(keypoints, 11, 13, 15) || PoseCalculations.HasFlexedKnee(keypoints, 12, 14, 16));
            detections.Add(new Detection(new Box(left, top, right - left, bottom - top), confidence, predictedClass,
                hipWeight > 0 ? hipX / hipWeight : centerX,
                hipWeight > 0 ? hipY / hipWeight : centerY,
                hipWeight > 0, seated));
        }
        return SuppressOverlaps(detections, .45f);
    }

    private static IReadOnlyList<Detection> SuppressOverlaps(List<Detection> candidates, float threshold)
    {
        var selected = new List<Detection>();
        foreach (var detection in candidates.OrderByDescending(item => item.Score))
        {
            if (selected.All(previous => previous.ClassId != detection.ClassId || Geometry.IoU(previous.Box, detection.Box) < threshold)) selected.Add(detection);
        }
        return selected;
    }

    public void Dispose() => _session.Dispose();
}
