using FoodVision.Shared.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FoodVision.Tables.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddTablesApplication(this IServiceCollection services)
    {
        services.AddHandlers(typeof(DependencyInjection).Assembly);
        return services;
    }
}
