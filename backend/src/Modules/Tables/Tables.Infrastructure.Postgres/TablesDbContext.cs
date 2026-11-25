using System.Text.Json;
using FoodVision.Orders.Contracts;
using FoodVision.Tables.Domain;
using Microsoft.EntityFrameworkCore;

namespace FoodVision.Tables.Infrastructure.Postgres;

public sealed class TablesDbContext(DbContextOptions<TablesDbContext> options) : DbContext(options)
{
    public const string Schema = "tables";

    public DbSet<TableSessionState> TableSessionStates => Set<TableSessionState>();
    public DbSet<TableEvent> TableEvents => Set<TableEvent>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<TableSessionState>(builder =>
        {
            builder.ToTable("table_session_states");
            builder.HasKey(t => new { t.SessionId, t.TableId });

            builder.Property(t => t.SessionId).HasColumnName("session_id").HasMaxLength(100);
            builder.Property(t => t.TableId).HasColumnName("table_id").HasMaxLength(100);
            builder.Property(t => t.RestaurantId).HasColumnName("restaurant_id").HasMaxLength(100);

            builder.Property(t => t.HasPeople).HasColumnName("has_people");
            builder.Property(t => t.PeopleCount).HasColumnName("people_count");
            builder.Property(t => t.HasBelongings).HasColumnName("has_belongings");
            builder.Property(t => t.HasOpenOrders).HasColumnName("has_open_orders");
            builder.Property(t => t.IsOccupied).HasColumnName("is_occupied");

            builder.Property(t => t.CleaningStatus).HasColumnName("cleaning_status").HasConversion<string>().HasMaxLength(50);
            builder.Property(t => t.VideoFreshness).HasColumnName("video_freshness").HasConversion<string>().HasMaxLength(50);
            builder.Property(t => t.OrdersFreshness).HasColumnName("orders_freshness").HasConversion<string>().HasMaxLength(50);

            builder.Property(t => t.LastVideoPositionSeconds).HasColumnName("last_video_position_seconds");
            builder.Property(t => t.LastVideoObservationTime).HasColumnName("last_video_observation_time");
            builder.Property(t => t.LastOrderUpdateTime).HasColumnName("last_order_update_time");

            builder.Property(t => t.WasUsedInSession).HasColumnName("was_used_in_session");
            builder.Property(t => t.Version).HasColumnName("version").IsConcurrencyToken();
            builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

            builder.Property<string>("_handledActionsJson")
                .HasColumnName("handled_action_ids")
                .HasColumnType("text")
                .HasDefaultValue("[]");

            builder.OwnsMany(t => t.Orders, orderBuilder =>
            {
                orderBuilder.ToTable("table_session_orders");
                orderBuilder.WithOwner().HasForeignKey("SessionId", "TableId");
                orderBuilder.Property<string>("SessionId").HasColumnName("session_id").HasMaxLength(100);
                orderBuilder.Property<string>("TableId").HasColumnName("table_id").HasMaxLength(100);
                orderBuilder.Property(o => o.OrderId).HasColumnName("order_id").HasMaxLength(100);
                orderBuilder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50);
                orderBuilder.Property(o => o.OpenedAt).HasColumnName("opened_at");
                orderBuilder.Property(o => o.PaidAt).HasColumnName("paid_at");
                orderBuilder.Property(o => o.Version).HasColumnName("version");
                orderBuilder.HasKey("SessionId", "TableId", "OrderId");
            });

            builder.Navigation(t => t.Orders).AutoInclude();
            builder.Ignore(t => t.UncommittedEvents);
            builder.Ignore(t => t.HandledActionIds);
        });

        modelBuilder.Entity<TableEvent>(builder =>
        {
            builder.ToTable("table_events");
            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id).HasColumnName("id");
            builder.Property(e => e.SessionId).HasColumnName("session_id").HasMaxLength(100);
            builder.Property(e => e.TableId).HasColumnName("table_id").HasMaxLength(100);
            builder.Property(e => e.EventType).HasColumnName("event_type").HasConversion<string>().HasMaxLength(50);
            builder.Property(e => e.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(50);
            builder.Property(e => e.VideoPositionSeconds).HasColumnName("video_position_seconds");
            builder.Property(e => e.Timestamp).HasColumnName("timestamp");
            builder.Property(e => e.Description).HasColumnName("description");
            builder.Property(e => e.ActionId).HasColumnName("action_id").HasMaxLength(100);
            builder.Property(e => e.Version).HasColumnName("version");
            builder.Property(e => e.CreatedAt).HasColumnName("created_at");

            builder.HasIndex(e => new { e.SessionId, e.TableId, e.VideoPositionSeconds });
            builder.HasIndex(e => new { e.SessionId, e.TableId, e.Timestamp });
            builder.HasIndex(e => new { e.SessionId, e.TableId, e.ActionId })
                .IsUnique()
                .HasFilter("action_id IS NOT NULL");
        });
    }
}
