using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace FoodVision.VideoAnalytics.Infrastructure.Postgres.Migrations;

[DbContext(typeof(VideoAnalyticsDbContext))]
[Migration("202611240001_InitializeVideoAnalyticsSchema")]
public partial class InitializeVideoAnalyticsSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: VideoAnalyticsDbContext.Schema);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS video_analytics CASCADE;");
    }
}
