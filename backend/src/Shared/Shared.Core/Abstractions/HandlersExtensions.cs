using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace FoodVision.Shared.Core.Abstractions;

public static class HandlersExtensions
{
    public static IServiceCollection AddHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        foreach (var type in assemblies.Distinct().SelectMany(assembly => assembly.GetExportedTypes())
                     .Where(type => type is { IsClass: true, IsAbstract: false } && !type.ContainsGenericParameters))
        {
            foreach (var contract in type.GetInterfaces().Where(contract => contract.IsGenericType &&
                         contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
                services.AddScoped(contract, type);
        }

        services.AddScoped<IDispatcher, Dispatcher>();
        return services;
    }
}
