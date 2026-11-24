using FoodVision.Api.Utilites;
using Microsoft.ML.OnnxRuntime;

namespace FoodVision.Api.Infrastructure;

internal sealed class PersonDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _input;

    public int InputWidth => 640;
    public int InputHeight => 640;

    public PersonDetector(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Не найдена модель обнаружения людей: {path}. Запустите scripts/setup-models.ps1.", path);
        using var options = Microsoft.ML.OnnxRuntime.SessionOptions.MakeSessionOptionWithCudaProvider(0);
        _session = new InferenceSession(path, options);
        _input = _session.InputMetadata.Keys.First();
        if (!_session.InputMetadata[_input].Dimensions.SequenceEqual(new[] { 1, 3, 640, 640 }))
        {
            _session.Dispose();
            throw new InvalidDataException("Модель YOLO26l должна принимать изображение 1×3×640×640.");
        }
    }

    public IReadOnlyList<Detection> Detect(InferenceInput input, IReadOnlyList<Detection> poses)
    {
        if (input.Width != InputWidth || input.Height != InputHeight)
            throw new ArgumentException("Размер входного тензора не совпадает с форматом YOLO26l.", nameof(input));

        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, input.Tensor)]);
        var output = results.First().AsTensor<float>();
        if (output.Dimensions.Length != 3 || output.Dimensions[0] != 1 || output.Dimensions[2] != 6)
            throw new InvalidDataException("Неизвестный формат выхода YOLO26l: ожидается [1,N,6].");
        var candidates = new List<Detection>();
        for (var i = 0; i < output.Dimensions[1]; i++)
        {
            var score = output[0, i, 4];
            if (!float.IsFinite(score) || score < .1f || output[0, i, 5] != 0) continue;
            var left = Math.Clamp((output[0, i, 0] - input.PadX) / input.Scale, 0, input.SourceWidth);
            var top = Math.Clamp((output[0, i, 1] - input.PadY) / input.Scale, 0, input.SourceHeight);
            var right = Math.Clamp((output[0, i, 2] - input.PadX) / input.Scale, 0, input.SourceWidth);
            var bottom = Math.Clamp((output[0, i, 3] - input.PadY) / input.Scale, 0, input.SourceHeight);
            if (right - left < 2 || bottom - top < 2) continue;
            candidates.Add(new Detection(new Box(left, top, right - left, bottom - top), score, 0, 0, 0, false, false));
        }
        var selected = new List<Detection>();
        foreach (var person in candidates.OrderByDescending(item => item.Score))
            if (selected.All(other => Geometry.IoU(person.Box, other.Box) < .45f)) selected.Add(person);

        // A pose contributes posture only to its best matching detected person, once.
        var pairs = (from personIndex in Enumerable.Range(0, selected.Count)
                     from poseIndex in Enumerable.Range(0, poses.Count)
                     let overlap = Geometry.IoU(selected[personIndex].Box, poses[poseIndex].Box)
                     where overlap >= .2f
                     orderby overlap descending
                     select (personIndex, poseIndex)).ToArray();
        var usedPeople = new HashSet<int>();
        var usedPoses = new HashSet<int>();
        foreach (var (personIndex, poseIndex) in pairs)
        {
            if (usedPeople.Contains(personIndex) || usedPoses.Contains(poseIndex)) continue;
            usedPeople.Add(personIndex); usedPoses.Add(poseIndex);
            var pose = poses[poseIndex];
            selected[personIndex] = selected[personIndex] with { HipX = pose.HipX, HipY = pose.HipY, HasHip = pose.HasHip, Seated = pose.Seated };
        }
        return selected;
    }

    public void Dispose() => _session.Dispose();
}
