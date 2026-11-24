using CSharpFunctionalExtensions;

namespace FoodVision.Shared.Core.Abstractions;

public interface IDispatcher
{
    Task<Result<TResponse, string>> Send<TResponse, TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand;
}
