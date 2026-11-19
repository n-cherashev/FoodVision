using CSharpFunctionalExtensions;

namespace FoodVision.Api.Features.Analysis;

public interface IRequest<T> { }
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<Result<TResponse, string>> Handle(TRequest request, CancellationToken cancellationToken);
}
public interface IDispatcher
{
    Task<Result<TResponse, string>> Dispatch<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken);
}

public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    public Task<Result<TResponse, string>> Dispatch<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), typeof(TResponse));
        dynamic handler = services.GetRequiredService(handlerType);
        return handler.Handle((dynamic)request, cancellationToken);
    }
}
