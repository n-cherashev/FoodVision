using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodVision.Orders.Infrastructure.Postgres.Migrations;

[DbContext(typeof(OrdersDbContext))]
[Migration("202611250002_InitializeOrdersSchema")]
public partial class InitializeOrdersSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: OrdersDbContext.Schema);

        migrationBuilder.CreateTable(
            name: "orders",
            schema: OrdersDbContext.Schema,
            columns: table => new
            {
                order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                order_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                restaurant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                table_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                waiter_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                waiter_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                guests_count = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                first_dish_served_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cancellation_reason = table.Column<string>(type: "text", nullable: true),
                video_position_seconds = table.Column<double>(type: "double precision", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_orders", x => x.order_id);
            });

        migrationBuilder.CreateTable(
            name: "order_items",
            schema: OrdersDbContext.Schema,
            columns: table => new
            {
                order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                item_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                dish_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                quantity = table.Column<int>(type: "integer", nullable: false),
                price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                course = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                served_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                video_position_seconds = table.Column<double>(type: "double precision", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_items", x => new { x.order_id, x.item_id });
                table.ForeignKey(
                    name: "FK_order_items_orders_order_id",
                    column: x => x.order_id,
                    principalSchema: OrdersDbContext.Schema,
                    principalTable: "orders",
                    principalColumn: "order_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_orders_session_id",
            schema: OrdersDbContext.Schema,
            table: "orders",
            column: "session_id");

        migrationBuilder.CreateIndex(
            name: "IX_orders_table_id",
            schema: OrdersDbContext.Schema,
            table: "orders",
            column: "table_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS orders CASCADE;");
    }
}
