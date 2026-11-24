using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FoodVision.VideoAnalytics.Infrastructure.Postgres;

public sealed class DesignTimeVideoAnalyticsDbContextFactory : IDesignTimeDbContextFactory<VideoAnalyticsDbContext>
{
    public VideoAnalyticsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__FoodVision")
            ?? Environment.GetEnvironmentVariable("FOODVISION_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings__FoodVision or FOODVISION_CONNECTION_STRING before running EF Core tools.");

        var options = new DbContextOptionsBuilder<VideoAnalyticsDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", VideoAnalyticsDbContext.Schema))
            .Options;

        return new VideoAnalyticsDbContext(options);
    }
}
