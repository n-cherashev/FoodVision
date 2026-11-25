namespace FoodVision.Orders.Contracts;

public sealed class TestVideoOrderEventsSource : IVideoOrderEventsSource
{
    private readonly string _restaurantId;
    private readonly List<OrderEvent> _events;

    public TestVideoOrderEventsSource(string restaurantId, IEnumerable<OrderEvent> events)
    {
        _restaurantId = restaurantId;
        _events = events.OrderBy(e => e.VideoPositionSeconds ?? 0).ThenBy(e => e.EventVersion).ToList();
    }

    public IReadOnlyList<OrderEvent> GetEventsUpTo(double videoPositionSeconds)
    {
        return _events.Where(e => (e.VideoPositionSeconds ?? 0) <= videoPositionSeconds).ToList();
    }

    public IReadOnlyList<OrderEvent> GetAllEvents() => _events.AsReadOnly();

    public OrdersSnapshot GetSnapshotAt(double videoPositionSeconds)
    {
        var relevantEvents = GetEventsUpTo(videoPositionSeconds);
        var orders = new Dictionary<string, OrderDto>();

        foreach (var ev in relevantEvents)
        {
            switch (ev.EventType)
            {
                case OrderEventType.Opened:
                    orders[ev.OrderId] = new OrderDto(
                        ev.OrderId,
                        ev.TableId,
                        ev.RestaurantId,
                        OrderStatus.Open,
                        ev.OpenedAt ?? ev.Timestamp,
                        null,
                        null,
                        ev.Timestamp,
                        ev.EventVersion);
                    break;

                case OrderEventType.Paid:
                    if (orders.TryGetValue(ev.OrderId, out var existingPaid))
                    {
                        orders[ev.OrderId] = existingPaid with
                        {
                            Status = OrderStatus.Paid,
                            PaidAt = ev.PaidAt ?? ev.Timestamp,
                            UpdatedAt = ev.Timestamp,
                            Version = ev.EventVersion
                        };
                    }
                    else
                    {
                        orders[ev.OrderId] = new OrderDto(
                            ev.OrderId,
                            ev.TableId,
                            ev.RestaurantId,
                            OrderStatus.Paid,
                            ev.OpenedAt ?? ev.Timestamp,
                            ev.PaidAt ?? ev.Timestamp,
                            null,
                            ev.Timestamp,
                            ev.EventVersion);
                    }
                    break;

                case OrderEventType.Closed:
                    if (orders.TryGetValue(ev.OrderId, out var existingClosed))
                    {
                        orders[ev.OrderId] = existingClosed with
                        {
                            Status = OrderStatus.Closed,
                            ClosedAt = ev.ClosedAt ?? ev.Timestamp,
                            UpdatedAt = ev.Timestamp,
                            Version = ev.EventVersion
                        };
                    }
                    break;

                case OrderEventType.Cancelled:
                    if (orders.TryGetValue(ev.OrderId, out var existingCancelled))
                    {
                        orders[ev.OrderId] = existingCancelled with
                        {
                            Status = OrderStatus.Cancelled,
                            UpdatedAt = ev.Timestamp,
                            Version = ev.EventVersion
                        };
                    }
                    break;

                case OrderEventType.Transferred:
                    if (orders.TryGetValue(ev.OrderId, out var existingTransferred) && !string.IsNullOrWhiteSpace(ev.TargetTableId))
                    {
                        orders[ev.OrderId] = existingTransferred with
                        {
                            TableId = ev.TargetTableId,
                            UpdatedAt = ev.Timestamp,
                            Version = ev.EventVersion
                        };
                    }
                    break;

                case OrderEventType.Updated:
                    if (orders.TryGetValue(ev.OrderId, out var existingUpdated))
                    {
                        orders[ev.OrderId] = existingUpdated with
                        {
                            UpdatedAt = ev.Timestamp,
                            Version = ev.EventVersion
                        };
                    }
                    break;
            }
        }

        var activeOrders = orders.Values
            .Where(o => o.Status == OrderStatus.Open || o.Status == OrderStatus.Paid)
            .ToList();

        var maxVersion = relevantEvents.Count > 0 ? relevantEvents.Max(e => e.EventVersion) : 0;
        return new OrdersSnapshot(_restaurantId, activeOrders, DateTimeOffset.UtcNow, maxVersion);
    }
}
