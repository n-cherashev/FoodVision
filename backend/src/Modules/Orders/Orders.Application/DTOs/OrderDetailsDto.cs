using FoodVision.Orders.Contracts;
using FoodVision.Orders.Domain;

namespace FoodVision.Orders.Application.DTOs;

public sealed record OrderDetailsDto(
    string OrderId,
    string OrderNumber,
    string RestaurantId,
    string TableId,
    string? SessionId,
    string? WaiterId,
    string? WaiterName,
    int GuestsCount,
    OrderStatus Status,
    decimal TotalAmount,
    decimal PaidAmount,
    DateTimeOffset OpenedAt,
    DateTimeOffset? FirstDishServedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    double? VideoPositionSeconds,
    long Version,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OrderItemDto> Items);

public sealed record OrderItemDto(
    string ItemId,
    string DishName,
    string Category,
    int Quantity,
    decimal Price,
    int Course,
    ItemPreparationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ServedAt,
    double? VideoPositionSeconds);

public static class OrderMapper
{
    public static OrderDetailsDto ToDetailsDto(this Order order)
    {
        return new OrderDetailsDto(
            order.OrderId,
            order.OrderNumber,
            order.RestaurantId,
            order.TableId,
            order.SessionId,
            order.WaiterId,
            order.WaiterName,
            order.GuestsCount,
            order.Status,
            order.TotalAmount,
            order.PaidAmount,
            order.OpenedAt,
            order.FirstDishServedAt,
            order.PaidAt,
            order.ClosedAt,
            order.CancelledAt,
            order.CancellationReason,
            order.VideoPositionSeconds,
            order.Version,
            order.UpdatedAt,
            order.Items.Select(i => new OrderItemDto(
                i.ItemId,
                i.DishName,
                i.Category,
                i.Quantity,
                i.Price,
                i.Course,
                i.Status,
                i.CreatedAt,
                i.ServedAt,
                i.VideoPositionSeconds)).ToList());
    }
}
