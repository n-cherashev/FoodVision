using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodVision.Tables.Infrastructure.Postgres.Migrations;

[DbContext(typeof(TablesDbContext))]
[Migration("202611260001_AddTableZones")]
public partial class AddTableZones : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "table_zones",
            schema: TablesDbContext.Schema,
            columns: table => new
            {
                id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                restaurant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                capacity = table.Column<int>(type: "integer", nullable: false),
                geometry = table.Column<string>(type: "jsonb", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_table_zones", x => x.id);
            });

        var now = DateTimeOffset.UtcNow.ToString("O");
        migrationBuilder.Sql($$"""
INSERT INTO tables.table_zones (id, restaurant_id, name, capacity, geometry, is_active, created_at, updated_at)
VALUES 
('T-01', 'default', 'T-01', 4, '{"display_polygon": [[455, 253], [403, 298], [305, 286], [366, 245]], "counting_polygon": [[326, 206], [511, 227], [444, 368], [249, 322]]}'::jsonb, true, '{{now}}', '{{now}}'),
('T-02', 'default', 'T-02', 4, '{"display_polygon": [[571, 278], [580, 263], [597, 261], [618, 255], [636, 253], [664, 253], [689, 259], [709, 270], [713, 285], [694, 294], [674, 300], [647, 302], [623, 298], [599, 296], [580, 291]], "counting_polygon": [[554, 211], [767, 213], [775, 412], [533, 404]]}'::jsonb, true, '{{now}}', '{{now}}'),
('T-03', 'default', 'T-03', 4, '{"display_polygon": [[909, 319], [1027, 330], [1067, 383], [934, 373]], "counting_polygon": [[865, 260], [1084, 264], [1108, 470], [859, 456]]}'::jsonb, true, '{{now}}', '{{now}}'),
('T-04', 'default', 'T-04', 4, '{"display_polygon": [[187, 450], [211, 433], [232, 422], [255, 418], [277, 414], [298, 414], [326, 416], [346, 423], [361, 435], [374, 450], [371, 470], [361, 485], [342, 502], [324, 511], [305, 517], [279, 526], [262, 528], [238, 525], [213, 519], [193, 508], [178, 498], [174, 481], [178, 467]], "counting_polygon": [[140, 350], [480, 408], [310, 690], [20, 599]]}'::jsonb, true, '{{now}}', '{{now}}'),
('T-05', 'default', 'T-05', 4, '{"display_polygon": [[515, 457], [689, 485], [649, 603], [444, 566]], "counting_polygon": [[487, 416], [782, 446], [700, 713], [372, 696]]}'::jsonb, true, '{{now}}', '{{now}}'),
('T-06', 'default', 'T-06', 4, '{"display_polygon": [[823, 500], [997, 511], [1020, 631], [822, 614]], "counting_polygon": [[799, 467], [1067, 484], [1059, 714], [754, 714]]}'::jsonb, true, '{{now}}', '{{now}}')
ON CONFLICT (id) DO NOTHING;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "table_zones",
            schema: TablesDbContext.Schema);
    }
}
