using FoodVision.Shared.Framework.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodVision.VideoAnalytics.Infrastructure.Postgres;

public sealed class VideoAnalyticsDatabaseMigrator(VideoAnalyticsDbContext dbContext) : IModuleDatabaseMigrator
{
    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        dbContext.Database.MigrateAsync(cancellationToken);
}
