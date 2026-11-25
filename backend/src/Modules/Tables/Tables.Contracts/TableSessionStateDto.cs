using FoodVision.Orders.Contracts;

namespace FoodVision.Tables.Contracts;

public sealed record TableSessionStateDto(
    string SessionId,
    string TableId,
    string RestaurantId,
    bool HasPeople,
    int PeopleCount,
    bool HasBelongings,
    bool HasOpenOrders,
    bool IsOccupied,
    string CleaningStatus,
    string VideoFreshness,
    string OrdersFreshness,
    double? LastVideoPositionSeconds,
    DateTimeOffset? LastVideoObservationTime,
    DateTimeOffset? LastOrderUpdateTime,
    bool WasUsedInSession,
    long Version,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TableOrderDto> ActiveOrders);

public sealed record TableOrderDto(
    string OrderId,
    OrderStatus Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? PaidAt,
    long Version);

public sealed record TableEventDto(
    Guid Id,
    string SessionId,
    string TableId,
    string EventType,
    string Source,
    double? VideoPositionSeconds,
    DateTimeOffset Timestamp,
    string Description,
    string? ActionId,
    long Version,
    DateTimeOffset CreatedAt);
