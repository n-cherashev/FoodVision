using CSharpFunctionalExtensions;
using FoodVision.Shared.Core.Abstractions;
using FoodVision.Tables.Application.Interfaces;
using FoodVision.Tables.Contracts;

namespace FoodVision.Tables.Application.Features;

public sealed record ConfirmTableCleaningCommand(
    string SessionId,
    string TableId,
    double VideoPositionSeconds,
    DateTimeOffset Timestamp,
    string? ActionId = null) : ICommand;

public sealed class ConfirmTableCleaningHandler(ITableSessionRepository repository)
    : ICommandHandler<TableSessionStateDto, ConfirmTableCleaningCommand>
{
    public async Task<Result<TableSessionStateDto, string>> Handle(
        ConfirmTableCleaningCommand command,
        CancellationToken cancellationToken = default)
    {
        var stateResult = await repository.GetBy(command.SessionId, command.TableId, cancellationToken);
        if (stateResult.IsFailure)
            return Result.Failure<TableSessionStateDto, string>(stateResult.Error);

        var state = stateResult.Value;
        var confirmResult = state.ConfirmCleaning(
            command.VideoPositionSeconds,
            command.Timestamp,
            command.ActionId);

        if (confirmResult.IsFailure)
            return Result.Failure<TableSessionStateDto, string>(confirmResult.Error);

        var saveResult = await repository.Save(state, cancellationToken);
        if (saveResult.IsFailure)
            return Result.Failure<TableSessionStateDto, string>(saveResult.Error);

        return Result.Success<TableSessionStateDto, string>(saveResult.Value.ToDto());
    }
}
