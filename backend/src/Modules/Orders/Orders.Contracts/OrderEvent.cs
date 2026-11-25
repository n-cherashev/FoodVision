namespace FoodVision.Orders.Contracts;

public sealed record OrderEvent(
    Guid EventId,
    long EventVersion,
    OrderEventType EventType,
    string OrderId,
    string TableId,
    string RestaurantId,
    string? TargetTableId,
    OrderStatus Status,
    DateTimeOffset Timestamp,
    double? VideoPositionSeconds,
    DateTimeOffset? OpenedAt = null,
    DateTimeOffset? PaidAt = null,
    DateTimeOffset? ClosedAt = null);
