using CSharpFunctionalExtensions;

namespace FoodVision.Shared.Core.Abstractions;

public interface ICommandHandler<TResponse, in TCommand> where TCommand : ICommand
{
    Task<Result<TResponse, string>> Handle(TCommand command, CancellationToken cancellationToken = default);
}
