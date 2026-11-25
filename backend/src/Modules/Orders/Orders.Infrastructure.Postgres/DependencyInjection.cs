using FoodVision.Orders.Application.Interfaces;
using FoodVision.Orders.Infrastructure.Postgres.Repositories;
using FoodVision.Shared.Framework.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodVision.Orders.Infrastructure.Postgres;

public static class DependencyInjection
{
    public static IServiceCollection AddOrdersPostgres(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FoodVision")
            ?? throw new InvalidOperationException(
                "Connection string 'ConnectionStrings:FoodVision' is required for PostgreSQL.");

        services.AddDbContextPool<OrdersDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IOrdersRepository, OrdersRepository>();
        services.AddScoped<IModuleDatabaseMigrator, OrdersDatabaseMigrator>();

        return services;
    }
}
