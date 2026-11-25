using CSharpFunctionalExtensions;

namespace FoodVision.Tables.Domain;

public sealed class TableEvent
{
    public Guid Id { get; private set; }
    public string SessionId { get; private set; } = null!;
    public string TableId { get; private set; } = null!;
    public TableEventType EventType { get; private set; }
    public TableEventSource Source { get; private set; }
    public double? VideoPositionSeconds { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public string Description { get; private set; } = null!;
    public string? ActionId { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private TableEvent() { }

    private TableEvent(
        Guid id,
        string sessionId,
        string tableId,
        TableEventType eventType,
        TableEventSource source,
        double? videoPositionSeconds,
        DateTimeOffset timestamp,
        string description,
        string? actionId,
        long version,
        DateTimeOffset createdAt)
    {
        Id = id;
        SessionId = sessionId;
        TableId = tableId;
        EventType = eventType;
        Source = source;
        VideoPositionSeconds = videoPositionSeconds;
        Timestamp = timestamp;
        Description = description;
        ActionId = actionId;
        Version = version;
        CreatedAt = createdAt;
    }

    public static Result<TableEvent, string> Create(
        Guid id,
        string sessionId,
        string tableId,
        TableEventType eventType,
        TableEventSource source,
        double? videoPositionSeconds,
        DateTimeOffset timestamp,
        string description,
        string? actionId = null,
        long version = 1)
    {
        if (id == Guid.Empty)
            return Result.Failure<TableEvent, string>("Идентификатор события не может быть пустым.");
        if (string.IsNullOrWhiteSpace(sessionId))
            return Result.Failure<TableEvent, string>("Идентификатор сессии не может быть пустым.");
        if (string.IsNullOrWhiteSpace(tableId))
            return Result.Failure<TableEvent, string>("Идентификатор стола не может быть пустым.");
        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure<TableEvent, string>("Описание события не может быть пустым.");
        if (videoPositionSeconds.HasValue && (!double.IsFinite(videoPositionSeconds.Value) || videoPositionSeconds.Value < 0))
            return Result.Failure<TableEvent, string>("Позиция видео должна быть неотрицательным конечным числом.");

        return Result.Success<TableEvent, string>(new TableEvent(
            id,
            sessionId.Trim(),
            tableId.Trim(),
            eventType,
            source,
            videoPositionSeconds,
            timestamp,
            description.Trim(),
            actionId?.Trim(),
            version,
            DateTimeOffset.UtcNow));
    }
}
