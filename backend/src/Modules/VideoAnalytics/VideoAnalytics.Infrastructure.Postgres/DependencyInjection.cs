using FoodVision.Shared.Framework.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodVision.VideoAnalytics.Infrastructure.Postgres;

public static class DependencyInjection
{
    public static IServiceCollection AddVideoAnalyticsPostgres(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FoodVision")
            ?? throw new InvalidOperationException(
                "Connection string 'ConnectionStrings:FoodVision' is required for PostgreSQL.");

        services.AddDbContextPool<VideoAnalyticsDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", VideoAnalyticsDbContext.Schema)));
        services.AddScoped<IModuleDatabaseMigrator, VideoAnalyticsDatabaseMigrator>();

        return services;
    }
}
