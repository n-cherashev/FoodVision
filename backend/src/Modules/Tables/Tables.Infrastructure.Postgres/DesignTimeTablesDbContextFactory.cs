using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FoodVision.Tables.Infrastructure.Postgres;

public sealed class DesignTimeTablesDbContextFactory : IDesignTimeDbContextFactory<TablesDbContext>
{
    public TablesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__FoodVision")
            ?? Environment.GetEnvironmentVariable("FOODVISION_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings__FoodVision or FOODVISION_CONNECTION_STRING before running EF Core tools.");

        var options = new DbContextOptionsBuilder<TablesDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", TablesDbContext.Schema))
            .Options;

        return new TablesDbContext(options);
    }
}
