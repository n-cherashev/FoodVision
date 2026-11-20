using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Options;

namespace FoodVision.Api.Infrastructure;

/// <summary>
/// Owns the process-wide model sessions and one reusable input tensor per
/// configured inference slot.
/// </summary>
internal sealed class InferenceRuntime : IDisposable
{
    private readonly Lazy<ModelSet> _models;
    private readonly AnalysisOptions _options;

    public InferenceRuntime(IOptions<AnalysisOptions> options)
    {
        _options = options.Value;
        _models = new Lazy<ModelSet>(CreateModels, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public YoloOnnxModel TableDetector => _models.Value.TableDetector;
    public YoloOnnxModel PersonPose => _models.Value.PersonPose;
    public PersonDetector PersonDetector => _models.Value.PersonDetector;

    public InferenceWorkspaceLease RentInput() => _models.Value.RentInput();

    public void Dispose()
    {
        if (_models.IsValueCreated) _models.Value.Dispose();
    }

    private ModelSet CreateModels()
    {
        YoloOnnxModel? tableDetector = null;
        YoloOnnxModel? personPose = null;
        PersonDetector? personDetector = null;
        try
        {
            tableDetector = new YoloOnnxModel(_options.TableDetectionModelPath, _options.FrameSize, pose: false);
            personPose = new YoloOnnxModel(_options.PersonPoseModelPath, _options.FrameSize, pose: true);
            personDetector = new PersonDetector(_options.PersonDetectionModelPath);
            return new ModelSet(tableDetector, personPose, personDetector, GpuInferenceGate.BenchmarkConcurrency);
        }
        catch
        {
            personDetector?.Dispose();
            personPose?.Dispose();
            tableDetector?.Dispose();
            throw;
        }
    }

    private sealed class ModelSet : IDisposable
    {
        private readonly ConcurrentBag<InferenceWorkspace> _inputs;

        public ModelSet(YoloOnnxModel tableDetector, YoloOnnxModel personPose, PersonDetector personDetector, int concurrency)
        {
            TableDetector = tableDetector;
            PersonPose = personPose;
            PersonDetector = personDetector;
            _inputs = new ConcurrentBag<InferenceWorkspace>(Enumerable.Range(0, concurrency)
                .Select(_ => new InferenceWorkspace(tableDetector, personPose, personDetector)));
        }

        public YoloOnnxModel TableDetector { get; }
        public YoloOnnxModel PersonPose { get; }
        public PersonDetector PersonDetector { get; }

        public InferenceWorkspaceLease RentInput()
        {
            if (!_inputs.TryTake(out var input)) throw new InvalidOperationException("Нет свободного рабочего буфера инференса.");
            return new InferenceWorkspaceLease(input, _inputs);
        }

        public void Dispose()
        {
            PersonDetector.Dispose();
            PersonPose.Dispose();
            TableDetector.Dispose();
        }
    }
}

internal sealed class InferenceWorkspaceLease(InferenceWorkspace input, ConcurrentBag<InferenceWorkspace> pool) : IDisposable
{
    private ConcurrentBag<InferenceWorkspace>? _pool = pool;

    public InferenceWorkspace Workspace { get; } = input;

    public void Dispose() => Interlocked.Exchange(ref _pool, null)?.Add(Workspace);
}
