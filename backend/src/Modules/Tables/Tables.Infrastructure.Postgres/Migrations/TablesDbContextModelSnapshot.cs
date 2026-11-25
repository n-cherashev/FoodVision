using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace FoodVision.Tables.Infrastructure.Postgres.Migrations;

[DbContext(typeof(TablesDbContext))]
public partial class TablesDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDefaultSchema(TablesDbContext.Schema)
            .HasAnnotation("ProductVersion", "10.0.5")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("FoodVision.Tables.Domain.TableSessionState", b =>
        {
            b.Property<string>("SessionId")
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("session_id");

            b.Property<string>("TableId")
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("table_id");

            b.Property<string>("RestaurantId")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("restaurant_id");

            b.Property<string>("CleaningStatus")
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnType("character varying(50)")
                .HasColumnName("cleaning_status");

            b.Property<bool>("HasBelongings")
                .HasColumnType("boolean")
                .HasColumnName("has_belongings");

            b.Property<bool>("HasOpenOrders")
                .HasColumnType("boolean")
                .HasColumnName("has_open_orders");

            b.Property<bool>("HasPeople")
                .HasColumnType("boolean")
                .HasColumnName("has_people");

            b.Property<bool>("IsOccupied")
                .HasColumnType("boolean")
                .HasColumnName("is_occupied");

            b.Property<DateTimeOffset?>("LastOrderUpdateTime")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("last_order_update_time");

            b.Property<DateTimeOffset?>("LastVideoObservationTime")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("last_video_observation_time");

            b.Property<double?>("LastVideoPositionSeconds")
                .HasColumnType("double precision")
                .HasColumnName("last_video_position_seconds");

            b.Property<string>("OrdersFreshness")
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnType("character varying(50)")
                .HasColumnName("orders_freshness");

            b.Property<int>("PeopleCount")
                .HasColumnType("integer")
                .HasColumnName("people_count");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at");

            b.Property<long>("Version")
                .IsConcurrencyToken()
                .HasColumnType("bigint")
                .HasColumnName("version");

            b.Property<string>("VideoFreshness")
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnType("character varying(50)")
                .HasColumnName("video_freshness");

            b.Property<bool>("WasUsedInSession")
                .HasColumnType("boolean")
                .HasColumnName("was_used_in_session");

            b.Property<string>("_handledActionsJson")
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasColumnType("text")
                .HasDefaultValue("[]")
                .HasColumnName("handled_action_ids");

            b.HasKey("SessionId", "TableId");

            b.ToTable("table_session_states", "tables");
        });

        modelBuilder.Entity("FoodVision.Tables.Domain.TableEvent", b =>
        {
            b.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid")
                .HasColumnName("id");

            b.Property<string>("ActionId")
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("action_id");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at");

            b.Property<string>("Description")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("description");

            b.Property<string>("EventType")
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnType("character varying(50)")
                .HasColumnName("event_type");

            b.Property<string>("SessionId")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("session_id");

            b.Property<string>("Source")
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnType("character varying(50)")
                .HasColumnName("source");

            b.Property<string>("TableId")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("table_id");

            b.Property<DateTimeOffset>("Timestamp")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("timestamp");

            b.Property<long>("Version")
                .HasColumnType("bigint")
                .HasColumnName("version");

            b.Property<double?>("VideoPositionSeconds")
                .HasColumnType("double precision")
                .HasColumnName("video_position_seconds");

            b.HasKey("Id");

            b.HasIndex("SessionId", "TableId", "Timestamp");

            b.HasIndex("SessionId", "TableId", "VideoPositionSeconds");

            b.HasIndex("SessionId", "TableId", "ActionId")
                .IsUnique()
                .HasFilter("action_id IS NOT NULL");

            b.ToTable("table_events", "tables");
        });

        modelBuilder.Entity("FoodVision.Tables.Domain.TableSessionState", b =>
        {
            b.OwnsMany("FoodVision.Tables.Domain.TableOrderInfo", "Orders", b1 =>
            {
                b1.Property<string>("SessionId")
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)")
                    .HasColumnName("session_id");

                b1.Property<string>("TableId")
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)")
                    .HasColumnName("table_id");

                b1.Property<string>("OrderId")
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)")
                    .HasColumnName("order_id");

                b1.Property<DateTimeOffset>("OpenedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("opened_at");

                b1.Property<DateTimeOffset?>("PaidAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("paid_at");

                b1.Property<string>("Status")
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasColumnType("character varying(50)")
                    .HasColumnName("status");

                b1.Property<long>("Version")
                    .HasColumnType("bigint")
                    .HasColumnName("version");

                b1.HasKey("SessionId", "TableId", "OrderId");

                b1.ToTable("table_session_orders", "tables");

                b1.WithOwner()
                    .HasForeignKey("SessionId", "TableId");
            });

            b.Navigation("Orders");
        });
    }
}
