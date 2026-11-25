using FoodVision.Shared.Framework.Database;
using FoodVision.Tables.Application.Interfaces;
using FoodVision.Tables.Infrastructure.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodVision.Tables.Infrastructure.Postgres;

public static class DependencyInjection
{
    public static IServiceCollection AddTablesPostgres(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FoodVision")
            ?? throw new InvalidOperationException(
                "Connection string 'ConnectionStrings:FoodVision' is required for PostgreSQL.");

        services.AddDbContextPool<TablesDbContext>(options =>
        {
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", TablesDbContext.Schema));
            options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        services.AddScoped<ITableSessionRepository, TableSessionRepository>();
        services.AddScoped<IModuleDatabaseMigrator, TablesDatabaseMigrator>();

        return services;
    }
}
