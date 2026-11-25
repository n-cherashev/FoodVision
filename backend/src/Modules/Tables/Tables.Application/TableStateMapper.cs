using FoodVision.Tables.Contracts;
using FoodVision.Tables.Domain;

namespace FoodVision.Tables.Application;

public static class TableStateMapper
{
    public static TableSessionStateDto ToDto(this TableSessionState state)
    {
        return new TableSessionStateDto(
            state.SessionId,
            state.TableId,
            state.RestaurantId,
            state.HasPeople,
            state.PeopleCount,
            state.HasBelongings,
            state.HasOpenOrders,
            state.IsOccupied,
            state.CleaningStatus.ToString(),
            state.VideoFreshness.ToString(),
            state.OrdersFreshness.ToString(),
            state.LastVideoPositionSeconds,
            state.LastVideoObservationTime,
            state.LastOrderUpdateTime,
            state.WasUsedInSession,
            state.Version,
            state.UpdatedAt,
            state.Orders.Select(o => new TableOrderDto(
                o.OrderId,
                o.Status,
                o.OpenedAt,
                o.PaidAt,
                o.Version)).ToList());
    }

    public static TableEventDto ToDto(this TableEvent evt)
    {
        return new TableEventDto(
            evt.Id,
            evt.SessionId,
            evt.TableId,
            evt.EventType.ToString(),
            evt.Source.ToString(),
            evt.VideoPositionSeconds,
            evt.Timestamp,
            evt.Description,
            evt.ActionId,
            evt.Version,
            evt.CreatedAt);
    }
}
