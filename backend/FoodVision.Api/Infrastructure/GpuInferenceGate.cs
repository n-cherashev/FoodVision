namespace FoodVision.Api.Infrastructure;

/// <summary>
/// Limits process-wide concurrent model calls, regardless of whether they come
/// from the benchmark or uploaded-video analysis.
/// </summary>
internal static class GpuInferenceGate
{
    private const int MaximumInferenceConcurrency = 100;
    private static readonly object ConfigurationSync = new();
    private static SemaphoreSlim _slots = new(1, 1);
    private static int _slotCount = 1;
    private static int _activeLeases;

    public static int BenchmarkConcurrency => Volatile.Read(ref _slotCount);

    public static void Configure(int requestedConcurrency)
    {
        var concurrency = Math.Clamp(requestedConcurrency, 1, MaximumInferenceConcurrency);
        lock (ConfigurationSync)
        {
            if (Volatile.Read(ref _activeLeases) != 0)
                throw new InvalidOperationException("Нельзя менять лимит GPU во время активного инференса.");

            _slots.Dispose();
            _slots = new SemaphoreSlim(concurrency, concurrency);
            Volatile.Write(ref _slotCount, concurrency);
        }
    }

    public static async ValueTask<IDisposable> EnterInferenceAsync(CancellationToken cancellationToken)
    {
        var slots = Volatile.Read(ref _slots);
        await slots.WaitAsync(cancellationToken);
        Interlocked.Increment(ref _activeLeases);
        return new Lease(() =>
        {
            slots.Release();
            Interlocked.Decrement(ref _activeLeases);
        });
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
