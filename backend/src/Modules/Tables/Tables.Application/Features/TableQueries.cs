using CSharpFunctionalExtensions;
using FoodVision.Shared.Core.Abstractions;
using FoodVision.Tables.Application.Interfaces;
using FoodVision.Tables.Contracts;

namespace FoodVision.Tables.Application.Features;

public sealed record GetTableSessionStateQuery(string SessionId, string TableId) : ICommand;

public sealed class GetTableSessionStateHandler(ITableSessionRepository repository)
    : ICommandHandler<TableSessionStateDto, GetTableSessionStateQuery>
{
    public async Task<Result<TableSessionStateDto, string>> Handle(
        GetTableSessionStateQuery query,
        CancellationToken cancellationToken = default)
    {
        var result = await repository.GetBy(query.SessionId, query.TableId, cancellationToken);
        if (result.IsFailure) return Result.Failure<TableSessionStateDto, string>(result.Error);
        return Result.Success<TableSessionStateDto, string>(result.Value.ToDto());
    }
}

public sealed record GetTableEventsQuery(string SessionId, string TableId) : ICommand;

public sealed class GetTableEventsHandler(ITableSessionRepository repository)
    : ICommandHandler<IReadOnlyList<TableEventDto>, GetTableEventsQuery>
{
    public async Task<Result<IReadOnlyList<TableEventDto>, string>> Handle(
        GetTableEventsQuery query,
        CancellationToken cancellationToken = default)
    {
        var events = await repository.GetEvents(query.SessionId, query.TableId, cancellationToken);
        return Result.Success<IReadOnlyList<TableEventDto>, string>(events.Select(e => e.ToDto()).ToList());
    }
}

public sealed record GetSessionTablesQuery(string SessionId) : ICommand;

public sealed class GetSessionTablesHandler(ITableSessionRepository repository)
    : ICommandHandler<IReadOnlyList<TableSessionStateDto>, GetSessionTablesQuery>
{
    public async Task<Result<IReadOnlyList<TableSessionStateDto>, string>> Handle(
        GetSessionTablesQuery query,
        CancellationToken cancellationToken = default)
    {
        var tables = await repository.GetSessionTables(query.SessionId, cancellationToken);
        return Result.Success<IReadOnlyList<TableSessionStateDto>, string>(tables.Select(t => t.ToDto()).ToList());
    }
}
