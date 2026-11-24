using Microsoft.EntityFrameworkCore;

namespace FoodVision.VideoAnalytics.Infrastructure.Postgres;

public sealed class VideoAnalyticsDbContext(DbContextOptions<VideoAnalyticsDbContext> options) : DbContext(options)
{
    public const string Schema = "video_analytics";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
    }
}
