using FoodVision.Orders.Domain;
using Microsoft.EntityFrameworkCore;

namespace FoodVision.Orders.Infrastructure.Postgres;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public const string Schema = "orders";

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Order>(builder =>
        {
            builder.ToTable("orders");
            builder.HasKey(o => o.OrderId);

            builder.Property(o => o.OrderId).HasColumnName("order_id").HasMaxLength(100);
            builder.Property(o => o.OrderNumber).HasColumnName("order_number").HasMaxLength(100);
            builder.Property(o => o.RestaurantId).HasColumnName("restaurant_id").HasMaxLength(100);
            builder.Property(o => o.TableId).HasColumnName("table_id").HasMaxLength(100);
            builder.Property(o => o.SessionId).HasColumnName("session_id").HasMaxLength(100);
            builder.Property(o => o.WaiterId).HasColumnName("waiter_id").HasMaxLength(100);
            builder.Property(o => o.WaiterName).HasColumnName("waiter_name").HasMaxLength(150);
            builder.Property(o => o.GuestsCount).HasColumnName("guests_count");
            builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50);
            builder.Property(o => o.TotalAmount).HasColumnName("total_amount").HasPrecision(18, 2);
            builder.Property(o => o.PaidAmount).HasColumnName("paid_amount").HasPrecision(18, 2);

            builder.Property(o => o.OpenedAt).HasColumnName("opened_at");
            builder.Property(o => o.FirstDishServedAt).HasColumnName("first_dish_served_at");
            builder.Property(o => o.PaidAt).HasColumnName("paid_at");
            builder.Property(o => o.ClosedAt).HasColumnName("closed_at");
            builder.Property(o => o.CancelledAt).HasColumnName("cancelled_at");
            builder.Property(o => o.CancellationReason).HasColumnName("cancellation_reason");
            builder.Property(o => o.VideoPositionSeconds).HasColumnName("video_position_seconds");

            builder.Property(o => o.Version).HasColumnName("version").IsConcurrencyToken();
            builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");

            builder.HasIndex(o => o.TableId);
            builder.HasIndex(o => o.SessionId);

            builder.HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey("OrderId")
                .OnDelete(DeleteBehavior.Cascade);

            builder.Navigation(o => o.Items).AutoInclude();
        });

        modelBuilder.Entity<OrderItem>(builder =>
        {
            builder.ToTable("order_items");
            builder.Property<string>("OrderId").HasColumnName("order_id").HasMaxLength(100);
            builder.Property(i => i.ItemId).HasColumnName("item_id").HasMaxLength(100);
            builder.HasKey("OrderId", "ItemId");

            builder.Property(i => i.DishName).HasColumnName("dish_name").HasMaxLength(200);
            builder.Property(i => i.Category).HasColumnName("category").HasMaxLength(100);
            builder.Property(i => i.Quantity).HasColumnName("quantity");
            builder.Property(i => i.Price).HasColumnName("price").HasPrecision(18, 2);
            builder.Property(i => i.Course).HasColumnName("course");
            builder.Property(i => i.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50);
            builder.Property(i => i.CreatedAt).HasColumnName("created_at");
            builder.Property(i => i.ServedAt).HasColumnName("served_at");
            builder.Property(i => i.VideoPositionSeconds).HasColumnName("video_position_seconds");
        });
    }
}
