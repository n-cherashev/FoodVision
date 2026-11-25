using CSharpFunctionalExtensions;
using FoodVision.Orders.Application.DTOs;
using FoodVision.Orders.Application.Interfaces;
using FoodVision.Orders.Contracts;
using FoodVision.Shared.Core.Abstractions;
using FoodVision.Tables.Application.Features;

namespace FoodVision.Orders.Application.Features;

public sealed record PayOrderCommand(
    string OrderId,
    decimal? Amount = null,
    DateTimeOffset? PaidAt = null,
    double? VideoPositionSeconds = null) : ICommand;

public sealed class PayOrderHandler(
    IOrdersRepository repository,
    IDispatcher dispatcher) : ICommandHandler<OrderDetailsDto, PayOrderCommand>
{
    public async Task<Result<OrderDetailsDto, string>> Handle(
        PayOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var orderResult = await repository.GetById(command.OrderId, cancellationToken);
        if (orderResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(orderResult.Error);

        var order = orderResult.Value;
        var payResult = order.Pay(command.Amount, command.PaidAt, command.VideoPositionSeconds);
        if (payResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(payResult.Error);

        var saveResult = await repository.Save(order, cancellationToken);
        if (saveResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(saveResult.Error);

        if (!string.IsNullOrWhiteSpace(order.SessionId))
        {
            var evt = order.ToEvent(OrderEventType.Paid, videoPositionSeconds: command.VideoPositionSeconds);
            await dispatcher.Send<FoodVision.Tables.Contracts.TableSessionStateDto, ApplyOrderEventCommand>(
                new(order.SessionId, evt, order.RestaurantId), cancellationToken);
        }

        return Result.Success<OrderDetailsDto, string>(saveResult.Value.ToDetailsDto());
    }
}

public sealed record CloseOrderCommand(
    string OrderId,
    DateTimeOffset? ClosedAt = null,
    double? VideoPositionSeconds = null) : ICommand;

public sealed class CloseOrderHandler(
    IOrdersRepository repository,
    IDispatcher dispatcher) : ICommandHandler<OrderDetailsDto, CloseOrderCommand>
{
    public async Task<Result<OrderDetailsDto, string>> Handle(
        CloseOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var orderResult = await repository.GetById(command.OrderId, cancellationToken);
        if (orderResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(orderResult.Error);

        var order = orderResult.Value;
        var closeResult = order.Close(command.ClosedAt, command.VideoPositionSeconds);
        if (closeResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(closeResult.Error);

        var saveResult = await repository.Save(order, cancellationToken);
        if (saveResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(saveResult.Error);

        if (!string.IsNullOrWhiteSpace(order.SessionId))
        {
            var evt = order.ToEvent(OrderEventType.Closed, videoPositionSeconds: command.VideoPositionSeconds);
            await dispatcher.Send<FoodVision.Tables.Contracts.TableSessionStateDto, ApplyOrderEventCommand>(
                new(order.SessionId, evt, order.RestaurantId), cancellationToken);
        }

        return Result.Success<OrderDetailsDto, string>(saveResult.Value.ToDetailsDto());
    }
}

public sealed record CancelOrderCommand(
    string OrderId,
    string Reason,
    DateTimeOffset? CancelledAt = null,
    double? VideoPositionSeconds = null) : ICommand;

public sealed class CancelOrderHandler(
    IOrdersRepository repository,
    IDispatcher dispatcher) : ICommandHandler<OrderDetailsDto, CancelOrderCommand>
{
    public async Task<Result<OrderDetailsDto, string>> Handle(
        CancelOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var orderResult = await repository.GetById(command.OrderId, cancellationToken);
        if (orderResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(orderResult.Error);

        var order = orderResult.Value;
        var cancelResult = order.Cancel(command.Reason, command.CancelledAt, command.VideoPositionSeconds);
        if (cancelResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(cancelResult.Error);

        var saveResult = await repository.Save(order, cancellationToken);
        if (saveResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(saveResult.Error);

        if (!string.IsNullOrWhiteSpace(order.SessionId))
        {
            var evt = order.ToEvent(OrderEventType.Cancelled, videoPositionSeconds: command.VideoPositionSeconds);
            await dispatcher.Send<FoodVision.Tables.Contracts.TableSessionStateDto, ApplyOrderEventCommand>(
                new(order.SessionId, evt, order.RestaurantId), cancellationToken);
        }

        return Result.Success<OrderDetailsDto, string>(saveResult.Value.ToDetailsDto());
    }
}

public sealed record TransferOrderCommand(
    string OrderId,
    string NewTableId,
    DateTimeOffset? TransferredAt = null,
    double? VideoPositionSeconds = null) : ICommand;

public sealed class TransferOrderHandler(
    IOrdersRepository repository,
    IDispatcher dispatcher) : ICommandHandler<OrderDetailsDto, TransferOrderCommand>
{
    public async Task<Result<OrderDetailsDto, string>> Handle(
        TransferOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var orderResult = await repository.GetById(command.OrderId, cancellationToken);
        if (orderResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(orderResult.Error);

        var order = orderResult.Value;
        var oldTableId = order.TableId;
        var transferResult = order.Transfer(command.NewTableId, command.TransferredAt, command.VideoPositionSeconds);
        if (transferResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(transferResult.Error);

        var saveResult = await repository.Save(order, cancellationToken);
        if (saveResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(saveResult.Error);

        if (!string.IsNullOrWhiteSpace(order.SessionId))
        {
            // Event has TableId = oldTableId, TargetTableId = NewTableId
            var evt = new OrderEvent(
                Guid.NewGuid(),
                order.Version,
                OrderEventType.Transferred,
                order.OrderId,
                oldTableId,
                order.RestaurantId,
                command.NewTableId,
                order.Status,
                order.UpdatedAt,
                command.VideoPositionSeconds ?? order.VideoPositionSeconds,
                order.OpenedAt,
                order.PaidAt,
                order.ClosedAt);

            await dispatcher.Send<FoodVision.Tables.Contracts.TableSessionStateDto, ApplyOrderEventCommand>(
                new(order.SessionId, evt, order.RestaurantId), cancellationToken);
        }

        return Result.Success<OrderDetailsDto, string>(saveResult.Value.ToDetailsDto());
    }
}
