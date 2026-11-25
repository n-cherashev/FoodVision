using FoodVision.Api.Utilites;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FoodVision.Api.Infrastructure;

/// <summary>
/// Experimental RF-DETR Keypoint Preview adapter. Its preprocessing and score
/// decoding mirror scripts/experiments/rfdetr/probe.py so the benchmark can
/// compare the one-model person pipeline with the current YOLO pair.
/// </summary>
internal sealed class RfDetrKeypointModel : IDisposable
{
    private const int InputSize = 576;
    private const int PersonLabel = 1;
    private const float TraceAlpha = .2f;
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly float _confidence;

    public RfDetrKeypointModel(string path, float confidence)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("RF-DETR Keypoint ONNX model was not found. Set Analysis:RfdetrKeypointModelPath for the benchmark container.", path);
        if (!float.IsFinite(confidence) || confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        try
        {
            using var options = SessionOptions.MakeSessionOptionWithCudaProvider(0);
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
            _session = new InferenceSession(path, options);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Unable to start RF-DETR Keypoint on NVIDIA GPU: {error.Message}", error);
        }
        _inputName = _session.InputMetadata.Keys.First();
        if (!_session.InputMetadata[_inputName].Dimensions.SequenceEqual(new[] { 1, 3, InputSize, InputSize }))
        {
            _session.Dispose();
            throw new InvalidDataException("RF-DETR Keypoint ONNX input must be [1,3,576,576].");
        }
        _confidence = confidence;
    }

    public IReadOnlyList<Detection> Detect(RfDetrKeypointInput input)
    {
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, input.Tensor)]);
        var output = results.ToDictionary(value => value.Name, value => value.AsTensor<float>(), StringComparer.Ordinal);
        if (!output.TryGetValue("dets", out var dets) || !output.TryGetValue("labels", out var labels) || !output.TryGetValue("keypoints", out var keypoints))
            throw new InvalidDataException("RF-DETR Keypoint output names must be dets, labels, and keypoints.");
        return Decode(dets, labels, keypoints, input.SourceWidth, input.SourceHeight, _confidence);
    }

    internal static IReadOnlyList<Detection> Decode(Tensor<float> dets, Tensor<float> labels, Tensor<float> keypoints, int sourceWidth, int sourceHeight, float confidenceThreshold)
    {
        ValidateOutput(dets, labels, keypoints);

        var candidates = new List<(float Score, int Index)>();
        for (var index = 0; index < labels.Dimensions[1]; index++)
            for (var label = 0; label < labels.Dimensions[2]; label++)
                candidates.Add((Sigmoid(labels[0, index, label]), index * labels.Dimensions[2] + label));

        var people = new List<Detection>();
        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Score).Take(100))
        {
            var index = candidate.Index / labels.Dimensions[2];
            var label = candidate.Index % labels.Dimensions[2];
            if (label != PersonLabel) continue;
            var score = CalibratedScore(candidate.Score, keypoints, index);
            if (score <= confidenceThreshold) continue;

            var centerX = dets[0, index, 0] * sourceWidth;
            var centerY = dets[0, index, 1] * sourceHeight;
            var width = dets[0, index, 2] * sourceWidth;
            var height = dets[0, index, 3] * sourceHeight;
            var left = Math.Clamp(centerX - width / 2, 0, sourceWidth);
            var top = Math.Clamp(centerY - height / 2, 0, sourceHeight);
            var right = Math.Clamp(centerX + width / 2, 0, sourceWidth);
            var bottom = Math.Clamp(centerY + height / 2, 0, sourceHeight);
            if (right - left < 2 || bottom - top < 2) continue;

            var points = new (float X, float Y, float Confidence)[17];
            var hipX = 0f;
            var hipY = 0f;
            var hipWeight = 0f;
            for (var joint = 0; joint < points.Length; joint++)
            {
                var raw = 17 + joint;
                var confidence = Sigmoid(keypoints[0, index, raw, 2]);
                var x = keypoints[0, index, raw, 0] * sourceWidth;
                var y = keypoints[0, index, raw, 1] * sourceHeight;
                points[joint] = (x, y, confidence);
                if (joint is 11 or 12 && confidence > .2f) { hipX += x * confidence; hipY += y * confidence; hipWeight += confidence; }
            }
            var seated = PoseCalculations.HasFlexedKnee(points, 11, 13, 15) || PoseCalculations.HasFlexedKnee(points, 12, 14, 16);
            people.Add(new Detection(new Box(left, top, right - left, bottom - top), score, 0,
                hipWeight > 0 ? hipX / hipWeight : centerX,
                hipWeight > 0 ? hipY / hipWeight : centerY,
                hipWeight > 0, seated));
        }
        return people;
    }

    private static void ValidateOutput(Tensor<float> dets, Tensor<float> labels, Tensor<float> keypoints)
    {
        if (!dets.Dimensions.SequenceEqual(new[] { 1, 100, 4 }) || !labels.Dimensions.SequenceEqual(new[] { 1, 100, 2 }) || !keypoints.Dimensions.SequenceEqual(new[] { 1, 100, 34, 8 }))
            throw new InvalidDataException($"Unexpected RF-DETR Keypoint outputs: dets=[{string.Join(',', dets.Dimensions.ToArray())}], labels=[{string.Join(',', labels.Dimensions.ToArray())}], keypoints=[{string.Join(',', keypoints.Dimensions.ToArray())}].");
    }

    private static float CalibratedScore(float labelScore, Tensor<float> keypoints, int index)
    {
        var numerator = double.NegativeInfinity;
        var denominator = double.NegativeInfinity;
        for (var joint = 0; joint < 17; joint++)
        {
            var raw = 17 + joint;
            var findability = Math.Max(Sigmoid(keypoints[0, index, raw, 2]), 1e-12f);
            var l11 = keypoints[0, index, raw, 4];
            var l21 = keypoints[0, index, raw, 5];
            var l22 = keypoints[0, index, raw, 6];
            var logTrace = LogAddExp(LogAddExp(-2 * l11, -2 * l22), 2 * Math.Log(Math.Max(Math.Abs(l21), 1e-12)) - 2 * l11 - 2 * l22);
            var logFindability = Math.Log(findability);
            numerator = LogAddExp(numerator, logTrace + logFindability);
            denominator = LogAddExp(denominator, logFindability);
        }
        return Sigmoid((float)(Math.Log(Math.Max(labelScore, 1e-12f)) - TraceAlpha * (numerator - denominator)));
    }

    private static double LogAddExp(double a, double b)
    {
        if (double.IsNegativeInfinity(a)) return b;
        if (double.IsNegativeInfinity(b)) return a;
        var maximum = Math.Max(a, b);
        return maximum + Math.Log(Math.Exp(a - maximum) + Math.Exp(b - maximum));
    }

    private static float Sigmoid(float value) => value >= 0 ? 1f / (1f + MathF.Exp(-value)) : MathF.Exp(value) / (1f + MathF.Exp(value));

    public void Dispose() => _session.Dispose();
}

/// <summary>Reusable RF-DETR image tensor: BGR -> RGB -> 576 square bilinear -> ImageNet normalization.</summary>
internal sealed class RfDetrKeypointInput
{
    private const int Size = 576;
    private static readonly float[] Mean = [.485f, .456f, .406f];
    private static readonly float[] StandardDeviation = [.229f, .224f, .225f];

    public DenseTensor<float> Tensor { get; } = new([1, 3, Size, Size]);
    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }

    public void Prepare(byte[] bgr, int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || bgr.Length < checked(sourceWidth * sourceHeight * 3))
            throw new ArgumentException("Invalid BGR frame.", nameof(bgr));
        var destination = Tensor.Buffer.Span;
        var plane = Size * Size;
        for (var y = 0; y < Size; y++)
        {
            var sourceY = (y + .5f) * sourceHeight / Size - .5f;
            var yFloor = (int)MathF.Floor(sourceY);
            var y0 = Math.Clamp(yFloor, 0, sourceHeight - 1);
            var y1 = Math.Clamp(yFloor + 1, 0, sourceHeight - 1);
            var wy = sourceY - yFloor;
            for (var x = 0; x < Size; x++)
            {
                var sourceX = (x + .5f) * sourceWidth / Size - .5f;
                var xFloor = (int)MathF.Floor(sourceX);
                var x0 = Math.Clamp(xFloor, 0, sourceWidth - 1);
                var x1 = Math.Clamp(xFloor + 1, 0, sourceWidth - 1);
                var wx = sourceX - xFloor;
                var target = y * Size + x;
                for (var channel = 0; channel < 3; channel++)
                {
                    var bgrChannel = 2 - channel;
                    var top = Lerp(bgr[(y0 * sourceWidth + x0) * 3 + bgrChannel], bgr[(y0 * sourceWidth + x1) * 3 + bgrChannel], wx);
                    var bottom = Lerp(bgr[(y1 * sourceWidth + x0) * 3 + bgrChannel], bgr[(y1 * sourceWidth + x1) * 3 + bgrChannel], wx);
                    destination[channel * plane + target] = (Lerp(top, bottom, wy) / 255f - Mean[channel]) / StandardDeviation[channel];
                }
            }
        }
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
    }

    private static float Lerp(float first, float second, float amount) => first + (second - first) * amount;
}
