namespace FoodVision.Tables.Domain;

public enum TableEventType
{
    PeopleArrived,
    PeopleLeft,
    ItemsPlaced,
    ItemsRemoved,
    OrderOpened,
    OrderPaid,
    OrderClosed,
    OrderCancelled,
    OrderTransferred,
    OccupancyChanged,
    CleaningRequired,
    CleaningConfirmed,
    FreshnessChanged
}

public enum TableEventSource
{
    VideoAnalytics,
    Orders,
    Manual
}
