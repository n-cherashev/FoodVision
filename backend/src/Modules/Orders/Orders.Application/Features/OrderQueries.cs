using CSharpFunctionalExtensions;
using FoodVision.Orders.Application.DTOs;
using FoodVision.Orders.Application.Interfaces;
using FoodVision.Shared.Core.Abstractions;

namespace FoodVision.Orders.Application.Features;

public sealed record GetOrderByIdQuery(string OrderId) : ICommand;

public sealed class GetOrderByIdHandler(IOrdersRepository repository)
    : ICommandHandler<OrderDetailsDto, GetOrderByIdQuery>
{
    public async Task<Result<OrderDetailsDto, string>> Handle(
        GetOrderByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var result = await repository.GetById(query.OrderId, cancellationToken);
        if (result.IsFailure) return Result.Failure<OrderDetailsDto, string>(result.Error);
        return Result.Success<OrderDetailsDto, string>(result.Value.ToDetailsDto());
    }
}

public sealed record GetOrdersByTableQuery(string TableId) : ICommand;

public sealed class GetOrdersByTableHandler(IOrdersRepository repository)
    : ICommandHandler<IReadOnlyList<OrderDetailsDto>, GetOrdersByTableQuery>
{
    public async Task<Result<IReadOnlyList<OrderDetailsDto>, string>> Handle(
        GetOrdersByTableQuery query,
        CancellationToken cancellationToken = default)
    {
        var list = await repository.GetByTable(query.TableId, cancellationToken);
        return Result.Success<IReadOnlyList<OrderDetailsDto>, string>(list.Select(o => o.ToDetailsDto()).ToList());
    }
}

public sealed record GetOrdersBySessionQuery(string SessionId) : ICommand;

public sealed class GetOrdersBySessionHandler(IOrdersRepository repository)
    : ICommandHandler<IReadOnlyList<OrderDetailsDto>, GetOrdersBySessionQuery>
{
    public async Task<Result<IReadOnlyList<OrderDetailsDto>, string>> Handle(
        GetOrdersBySessionQuery query,
        CancellationToken cancellationToken = default)
    {
        var list = await repository.GetBySession(query.SessionId, cancellationToken);
        return Result.Success<IReadOnlyList<OrderDetailsDto>, string>(list.Select(o => o.ToDetailsDto()).ToList());
    }
}
