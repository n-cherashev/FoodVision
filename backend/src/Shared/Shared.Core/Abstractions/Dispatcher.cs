using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;

namespace FoodVision.Shared.Core.Abstractions;

public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    public Task<Result<TResponse, string>> Send<TResponse, TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand =>
        services.GetRequiredService<ICommandHandler<TResponse, TCommand>>().Handle(command, cancellationToken);
}
