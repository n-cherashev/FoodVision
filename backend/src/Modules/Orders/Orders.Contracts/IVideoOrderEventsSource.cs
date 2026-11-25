namespace FoodVision.Orders.Contracts;

public interface IVideoOrderEventsSource
{
    IReadOnlyList<OrderEvent> GetEventsUpTo(double videoPositionSeconds);
    IReadOnlyList<OrderEvent> GetAllEvents();
    OrdersSnapshot GetSnapshotAt(double videoPositionSeconds);
}
