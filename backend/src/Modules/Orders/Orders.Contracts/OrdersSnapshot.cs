namespace FoodVision.Orders.Contracts;

public sealed record OrdersSnapshot(
    string RestaurantId,
    IReadOnlyList<OrderDto> Orders,
    DateTimeOffset SnapshotAt,
    long Version);
