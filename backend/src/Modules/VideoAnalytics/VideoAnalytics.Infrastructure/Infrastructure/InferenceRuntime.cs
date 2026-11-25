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
    public YoloOnnxModel? PersonPose => _models.Value.PersonPose;
    public PersonDetector? PersonDetector => _models.Value.PersonDetector;
    public RfDetrKeypointModel? RfDetrKeypoint => _models.Value.RfDetrKeypoint;

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
        RfDetrKeypointModel? rfdetrKeypoint = null;
        try
        {
            tableDetector = new YoloOnnxModel(_options.TableDetectionModelPath, _options.FrameSize, pose: false);
            if (!string.IsNullOrWhiteSpace(_options.RfdetrKeypointModelPath) && File.Exists(_options.RfdetrKeypointModelPath))
            {
                rfdetrKeypoint = new RfDetrKeypointModel(_options.RfdetrKeypointModelPath, _options.RfdetrKeypointConfidence);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(_options.PersonPoseModelPath) && File.Exists(_options.PersonPoseModelPath))
                {
                    personPose = new YoloOnnxModel(_options.PersonPoseModelPath, _options.FrameSize, pose: true);
                }
                if (!string.IsNullOrWhiteSpace(_options.PersonDetectionModelPath) && File.Exists(_options.PersonDetectionModelPath))
                {
                    personDetector = new PersonDetector(_options.PersonDetectionModelPath);
                }
            }
            return new ModelSet(tableDetector, personPose, personDetector, rfdetrKeypoint, GpuInferenceGate.BenchmarkConcurrency);
        }
        catch
        {
            rfdetrKeypoint?.Dispose();
            personDetector?.Dispose();
            personPose?.Dispose();
            tableDetector?.Dispose();
            throw;
        }
    }

    private sealed class ModelSet : IDisposable
    {
        private readonly ConcurrentBag<InferenceWorkspace> _inputs;

        public ModelSet(YoloOnnxModel tableDetector, YoloOnnxModel? personPose, PersonDetector? personDetector, RfDetrKeypointModel? rfdetrKeypoint, int concurrency)
        {
            TableDetector = tableDetector;
            PersonPose = personPose;
            PersonDetector = personDetector;
            RfDetrKeypoint = rfdetrKeypoint;
            _inputs = new ConcurrentBag<InferenceWorkspace>(Enumerable.Range(0, concurrency)
                .Select(_ => new InferenceWorkspace(tableDetector, personPose, personDetector, rfdetrKeypoint is not null)));
        }

        public YoloOnnxModel TableDetector { get; }
        public YoloOnnxModel? PersonPose { get; }
        public PersonDetector? PersonDetector { get; }
        public RfDetrKeypointModel? RfDetrKeypoint { get; }

        public InferenceWorkspaceLease RentInput()
        {
            if (!_inputs.TryTake(out var input)) throw new InvalidOperationException("Нет свободного рабочего буфера инференса.");
            return new InferenceWorkspaceLease(input, _inputs);
        }

        public void Dispose()
        {
            PersonDetector?.Dispose();
            PersonPose?.Dispose();
            RfDetrKeypoint?.Dispose();
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
