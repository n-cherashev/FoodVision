using CSharpFunctionalExtensions;
using FoodVision.Shared.Core.Abstractions;
using FoodVision.Tables.Application.Interfaces;
using FoodVision.Tables.Contracts;
using FoodVision.Tables.Domain;

namespace FoodVision.Tables.Application.Features;

public sealed record ApplyVideoObservationCommand(
    string SessionId,
    string TableId,
    bool? HasPeople,
    int? PeopleCount,
    bool? HasBelongings,
    DataFreshness Freshness,
    double VideoPositionSeconds,
    DateTimeOffset Timestamp,
    string? ActionId = null,
    string RestaurantId = "default") : ICommand;

public sealed class ApplyVideoObservationHandler(ITableSessionRepository repository)
    : ICommandHandler<TableSessionStateDto, ApplyVideoObservationCommand>
{
    public async Task<Result<TableSessionStateDto, string>> Handle(
        ApplyVideoObservationCommand command,
        CancellationToken cancellationToken = default)
    {
        var existingResult = await repository.GetBy(command.SessionId, command.TableId, cancellationToken);
        TableSessionState state;

        if (existingResult.IsSuccess)
        {
            state = existingResult.Value;
        }
        else
        {
            var createResult = TableSessionState.Create(command.SessionId, command.TableId, command.RestaurantId);
            if (createResult.IsFailure)
                return Result.Failure<TableSessionStateDto, string>(createResult.Error);
            state = createResult.Value;
        }

        var applyResult = state.ApplyVideoObservation(
            command.HasPeople,
            command.PeopleCount,
            command.HasBelongings,
            command.Freshness,
            command.VideoPositionSeconds,
            command.Timestamp,
            command.ActionId);

        if (applyResult.IsFailure)
            return Result.Failure<TableSessionStateDto, string>(applyResult.Error);

        var saveResult = await repository.Save(state, cancellationToken);
        if (saveResult.IsFailure)
            return Result.Failure<TableSessionStateDto, string>(saveResult.Error);

        return Result.Success<TableSessionStateDto, string>(saveResult.Value.ToDto());
    }
}
