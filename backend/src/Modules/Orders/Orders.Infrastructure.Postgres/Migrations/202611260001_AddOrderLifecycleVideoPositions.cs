using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodVision.Orders.Infrastructure.Postgres.Migrations;

[DbContext(typeof(OrdersDbContext))]
[Migration("202611260001_AddOrderLifecycleVideoPositions")]
public partial class AddOrderLifecycleVideoPositions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "paid_video_position_seconds",
            schema: OrdersDbContext.Schema,
            table: "orders",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "closed_video_position_seconds",
            schema: OrdersDbContext.Schema,
            table: "orders",
            type: "double precision",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "paid_video_position_seconds",
            schema: OrdersDbContext.Schema,
            table: "orders");

        migrationBuilder.DropColumn(
            name: "closed_video_position_seconds",
            schema: OrdersDbContext.Schema,
            table: "orders");
    }
}
