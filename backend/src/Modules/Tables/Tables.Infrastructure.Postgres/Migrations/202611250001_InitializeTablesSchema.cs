using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodVision.Tables.Infrastructure.Postgres.Migrations;

[DbContext(typeof(TablesDbContext))]
[Migration("202611250001_InitializeTablesSchema")]
public partial class InitializeTablesSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: TablesDbContext.Schema);

        migrationBuilder.CreateTable(
            name: "table_session_states",
            schema: TablesDbContext.Schema,
            columns: table => new
            {
                session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                table_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                restaurant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                has_people = table.Column<bool>(type: "boolean", nullable: false),
                people_count = table.Column<int>(type: "integer", nullable: false),
                has_belongings = table.Column<bool>(type: "boolean", nullable: false),
                has_open_orders = table.Column<bool>(type: "boolean", nullable: false),
                is_occupied = table.Column<bool>(type: "boolean", nullable: false),
                cleaning_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                video_freshness = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                orders_freshness = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                last_video_position_seconds = table.Column<double>(type: "double precision", nullable: true),
                last_video_observation_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_order_update_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                was_used_in_session = table.Column<bool>(type: "boolean", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                handled_action_ids = table.Column<string>(type: "text", nullable: false, defaultValue: "[]")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_table_session_states", x => new { x.session_id, x.table_id });
            });

        migrationBuilder.CreateTable(
            name: "table_session_orders",
            schema: TablesDbContext.Schema,
            columns: table => new
            {
                session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                table_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_table_session_orders", x => new { x.session_id, x.table_id, x.order_id });
                table.ForeignKey(
                    name: "FK_table_session_orders_table_session_states",
                    columns: x => new { x.session_id, x.table_id },
                    principalSchema: TablesDbContext.Schema,
                    principalTable: "table_session_states",
                    principalColumns: new[] { "session_id", "table_id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "table_events",
            schema: TablesDbContext.Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                table_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                video_position_seconds = table.Column<double>(type: "double precision", nullable: true),
                timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                action_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_table_events", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_table_events_session_id_table_id_video_position_seconds",
            schema: TablesDbContext.Schema,
            table: "table_events",
            columns: new[] { "session_id", "table_id", "video_position_seconds" });

        migrationBuilder.CreateIndex(
            name: "IX_table_events_session_id_table_id_timestamp",
            schema: TablesDbContext.Schema,
            table: "table_events",
            columns: new[] { "session_id", "table_id", "timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_table_events_session_id_table_id_action_id",
            schema: TablesDbContext.Schema,
            table: "table_events",
            columns: new[] { "session_id", "table_id", "action_id" },
            unique: true,
            filter: "action_id IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS tables CASCADE;");
    }
}
