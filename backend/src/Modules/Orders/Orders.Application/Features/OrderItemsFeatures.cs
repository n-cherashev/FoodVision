using CSharpFunctionalExtensions;
using FoodVision.Orders.Application.DTOs;
using FoodVision.Orders.Application.Interfaces;
using FoodVision.Shared.Core.Abstractions;

namespace FoodVision.Orders.Application.Features;

public sealed record AddOrderItemCommand(
    string OrderId,
    string ItemId,
    string DishName,
    string Category,
    int Quantity,
    decimal Price,
    int Course = 1,
    double? VideoPositionSeconds = null,
    DateTimeOffset? Timestamp = null) : ICommand;

public sealed class AddOrderItemHandler(IOrdersRepository repository)
    : ICommandHandler<OrderDetailsDto, AddOrderItemCommand>
{
    public async Task<Result<OrderDetailsDto, string>> Handle(
        AddOrderItemCommand command,
        CancellationToken cancellationToken = default)
    {
        var orderResult = await repository.GetById(command.OrderId, cancellationToken);
        if (orderResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(orderResult.Error);

        var order = orderResult.Value;
        var addResult = order.AddItem(
            command.ItemId,
            command.DishName,
            command.Category,
            command.Quantity,
            command.Price,
            command.Course,
            command.VideoPositionSeconds,
            command.Timestamp);

        if (addResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(addResult.Error);

        var saveResult = await repository.Save(order, cancellationToken);
        if (saveResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(saveResult.Error);

        return Result.Success<OrderDetailsDto, string>(saveResult.Value.ToDetailsDto());
    }
}

public sealed record MarkOrderItemServedCommand(
    string OrderId,
    string ItemId,
    DateTimeOffset? ServedAt = null,
    double? VideoPositionSeconds = null) : ICommand;

public sealed class MarkOrderItemServedHandler(IOrdersRepository repository)
    : ICommandHandler<OrderDetailsDto, MarkOrderItemServedCommand>
{
    public async Task<Result<OrderDetailsDto, string>> Handle(
        MarkOrderItemServedCommand command,
        CancellationToken cancellationToken = default)
    {
        var orderResult = await repository.GetById(command.OrderId, cancellationToken);
        if (orderResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(orderResult.Error);

        var order = orderResult.Value;
        var serveResult = order.MarkItemServed(
            command.ItemId,
            command.ServedAt ?? DateTimeOffset.UtcNow,
            command.VideoPositionSeconds);

        if (serveResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(serveResult.Error);

        var saveResult = await repository.Save(order, cancellationToken);
        if (saveResult.IsFailure) return Result.Failure<OrderDetailsDto, string>(saveResult.Error);

        return Result.Success<OrderDetailsDto, string>(saveResult.Value.ToDetailsDto());
    }
}
