using FoodVision.Api.Domain;

namespace FoodVision.Api.Utilites;

internal static class TimelineCalculations
{
    public static IReadOnlyList<TimeSegment> BuildSegments(IReadOnlyList<(double Time, TableState State)> samples, double interval, double duration)
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

}
