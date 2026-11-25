using FoodVision.Shared.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FoodVision.Orders.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOrdersApplication(this IServiceCollection services)
    {
        services.AddHandlers(typeof(DependencyInjection).Assembly);
        return services;
    }
}
