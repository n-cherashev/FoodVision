namespace FoodVision.Orders.Contracts;

public sealed record OrderDto(
    string OrderId,
    string TableId,
    string RestaurantId,
    OrderStatus Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset UpdatedAt,
    long Version);
