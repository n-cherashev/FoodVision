using CSharpFunctionalExtensions;

namespace FoodVision.Api.Domain;

public enum TableState { Occupied, Available, Review }

public sealed class BoundingBox
{
    private BoundingBox(double x, double y, double width, double height) => (X, Y, Width, Height) = (x, y, width, height);
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public static Result<BoundingBox, string> Create(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height))
            return Result.Failure<BoundingBox, string>("Координаты стола должны быть конечными числами.");
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > 1 || y + height > 1)
            return Result.Failure<BoundingBox, string>("Границы стола должны находиться внутри кадра.");
        return Result.Success<BoundingBox, string>(new BoundingBox(x, y, width, height));
    }
}

public sealed class TimeSegment
{
    private TimeSegment(double startSeconds, double endSeconds, TableState state) => (StartSeconds, EndSeconds, State) = (startSeconds, endSeconds, state);
    public double StartSeconds { get; }
    public double EndSeconds { get; }
    public TableState State { get; }

    public static Result<TimeSegment, string> Create(double startSeconds, double endSeconds, TableState state)
    {
        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) || startSeconds < 0 || endSeconds <= startSeconds)
            return Result.Failure<TimeSegment, string>("Интервал состояния должен иметь положительную длительность.");
        if (!Enum.IsDefined(state)) return Result.Failure<TimeSegment, string>("Неизвестное состояние стола.");
        return Result.Success<TimeSegment, string>(new TimeSegment(startSeconds, endSeconds, state));
    }
}

public sealed class TableAnalysis
{
    private TableAnalysis(string id, BoundingBox bounds, IReadOnlyList<TimeSegment> segments) => (Id, Bounds, Segments) = (id, bounds, segments);
    public string Id { get; }
    public BoundingBox Bounds { get; }
    public IReadOnlyList<TimeSegment> Segments { get; }

    public static Result<TableAnalysis, string> Create(string id, BoundingBox bounds, IReadOnlyList<TimeSegment> segments)
    {
        if (string.IsNullOrWhiteSpace(id)) return Result.Failure<TableAnalysis, string>("У стола должен быть идентификатор.");
        if (segments.Count == 0) return Result.Failure<TableAnalysis, string>("Для стола нужен хотя бы один временной интервал.");
        for (var index = 1; index < segments.Count; index++)
            if (segments[index].StartSeconds < segments[index - 1].EndSeconds)
                return Result.Failure<TableAnalysis, string>("Временные интервалы стола не должны перекрываться.");
        return Result.Success<TableAnalysis, string>(new TableAnalysis(id, bounds, segments));
    }
}

public sealed class VideoAnalysis
{
    private VideoAnalysis(double durationSeconds, IReadOnlyList<TableAnalysis> tables) => (DurationSeconds, Tables) = (durationSeconds, tables);
    public double DurationSeconds { get; }
    public IReadOnlyList<TableAnalysis> Tables { get; }

    public static Result<VideoAnalysis, string> Create(double durationSeconds, IReadOnlyList<TableAnalysis> tables)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            return Result.Failure<VideoAnalysis, string>("Длительность видео должна быть больше нуля.");
        if (tables.Any(table => table.Segments.Any(segment => segment.EndSeconds > durationSeconds + .001)))
            return Result.Failure<VideoAnalysis, string>("Интервал состояния выходит за длительность видео.");
        return Result.Success<VideoAnalysis, string>(new VideoAnalysis(durationSeconds, tables));
    }
}
