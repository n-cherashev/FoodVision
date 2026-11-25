using CSharpFunctionalExtensions;
using FoodVision.Orders.Contracts;
using FoodVision.Shared.Core.Abstractions;
using FoodVision.Tables.Application.Interfaces;
using FoodVision.Tables.Contracts;
using FoodVision.Tables.Domain;

namespace FoodVision.Tables.Application.Features;

public sealed record ApplyOrderEventCommand(
    string SessionId,
    OrderEvent OrderEvent,
    string RestaurantId = "default") : ICommand;

public sealed class ApplyOrderEventHandler(ITableSessionRepository repository)
    : ICommandHandler<TableSessionStateDto, ApplyOrderEventCommand>
{
    public async Task<Result<TableSessionStateDto, string>> Handle(
        ApplyOrderEventCommand command,
        CancellationToken cancellationToken = default)
    {
        var orderEvent = command.OrderEvent;
        if (orderEvent.EventType == OrderEventType.Transferred && !string.IsNullOrWhiteSpace(orderEvent.TargetTableId))
        {
            // Transfer recalculates both tables: source and target!
            var sourceState = await GetOrCreate(command.SessionId, orderEvent.TableId, command.RestaurantId, cancellationToken);
            if (sourceState.IsFailure) return Result.Failure<TableSessionStateDto, string>(sourceState.Error);

            var targetState = await GetOrCreate(command.SessionId, orderEvent.TargetTableId, command.RestaurantId, cancellationToken);
            if (targetState.IsFailure) return Result.Failure<TableSessionStateDto, string>(targetState.Error);

            var applySource = sourceState.Value.ApplyOrderEvent(orderEvent);
            if (applySource.IsFailure) return Result.Failure<TableSessionStateDto, string>(applySource.Error);

            var applyTarget = targetState.Value.ApplyOrderEvent(orderEvent);
            if (applyTarget.IsFailure) return Result.Failure<TableSessionStateDto, string>(applyTarget.Error);

            var saveSource = await repository.Save(sourceState.Value, cancellationToken);
            if (saveSource.IsFailure) return Result.Failure<TableSessionStateDto, string>(saveSource.Error);

            var saveTarget = await repository.Save(targetState.Value, cancellationToken);
            if (saveTarget.IsFailure) return Result.Failure<TableSessionStateDto, string>(saveTarget.Error);

            return Result.Success<TableSessionStateDto, string>(saveSource.Value.ToDto());
        }

        var stateResult = await GetOrCreate(command.SessionId, orderEvent.TableId, command.RestaurantId, cancellationToken);
        if (stateResult.IsFailure) return Result.Failure<TableSessionStateDto, string>(stateResult.Error);

        var applySingle = stateResult.Value.ApplyOrderEvent(orderEvent);
        if (applySingle.IsFailure) return Result.Failure<TableSessionStateDto, string>(applySingle.Error);

        var saveSingle = await repository.Save(stateResult.Value, cancellationToken);
        if (saveSingle.IsFailure) return Result.Failure<TableSessionStateDto, string>(saveSingle.Error);

        return Result.Success<TableSessionStateDto, string>(saveSingle.Value.ToDto());
    }

    private async Task<Result<TableSessionState, string>> GetOrCreate(
        string sessionId,
        string tableId,
        string restaurantId,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetBy(sessionId, tableId, cancellationToken);
        if (existing.IsSuccess) return existing;

        var createResult = TableSessionState.Create(sessionId, tableId, restaurantId);
        return createResult;
    }
}
