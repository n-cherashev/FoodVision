using CSharpFunctionalExtensions;
using FoodVision.Orders.Application.DTOs;
using FoodVision.Orders.Application.Interfaces;
using FoodVision.Orders.Contracts;
using FoodVision.Orders.Domain;
using FoodVision.Shared.Core.Abstractions;
using FoodVision.Tables.Application.Features;

namespace FoodVision.Orders.Application.Features;

public sealed record OpenOrderCommand(
    string OrderId,
    string TableId,
    string RestaurantId = "default",
    string? OrderNumber = null,
    string? SessionId = null,
    string? WaiterId = null,
    string? WaiterName = null,
    int GuestsCount = 1,
    double? VideoPositionSeconds = null,
    DateTimeOffset? OpenedAt = null) : ICommand;

public sealed class OpenOrderHandler(
    IOrdersRepository repository,
    IDispatcher dispatcher) : ICommandHandler<OrderDetailsDto, OpenOrderCommand>
{
    public async Task<Result<OrderDetailsDto, string>> Handle(
        OpenOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var orderResult = Order.Create(
            command.OrderId,
            command.TableId,
            command.RestaurantId,
            command.OrderNumber,
            command.SessionId,
            command.WaiterId,
            command.WaiterName,
            command.GuestsCount,
            command.VideoPositionSeconds,
            command.OpenedAt);

        if (orderResult.IsFailure)
            return Result.Failure<OrderDetailsDto, string>(orderResult.Error);

        var order = orderResult.Value;
        var saveResult = await repository.Save(order, cancellationToken);
        if (saveResult.IsFailure)
            return Result.Failure<OrderDetailsDto, string>(saveResult.Error);

        // Notify Tables module if bound to a session
        if (!string.IsNullOrWhiteSpace(order.SessionId))
        {
            var evt = order.ToEvent(OrderEventType.Opened);
            await dispatcher.Send<FoodVision.Tables.Contracts.TableSessionStateDto, ApplyOrderEventCommand>(
                new(order.SessionId, evt, order.RestaurantId), cancellationToken);
        }

        return Result.Success<OrderDetailsDto, string>(saveResult.Value.ToDetailsDto());
    }
}
