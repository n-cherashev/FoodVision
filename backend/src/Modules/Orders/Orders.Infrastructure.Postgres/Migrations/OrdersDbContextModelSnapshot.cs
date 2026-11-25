using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace FoodVision.Orders.Infrastructure.Postgres.Migrations;

[DbContext(typeof(OrdersDbContext))]
public partial class OrdersDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDefaultSchema(OrdersDbContext.Schema)
            .HasAnnotation("ProductVersion", "10.0.5")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("FoodVision.Orders.Domain.Order", b =>
        {
            b.Property<string>("OrderId")
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("order_id");

            b.Property<DateTimeOffset?>("CancelledAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("cancelled_at");

            b.Property<string>("CancellationReason")
                .HasColumnType("text")
                .HasColumnName("cancellation_reason");

            b.Property<DateTimeOffset?>("ClosedAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("closed_at");

            b.Property<DateTimeOffset?>("FirstDishServedAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("first_dish_served_at");

            b.Property<int>("GuestsCount")
                .HasColumnType("integer")
                .HasColumnName("guests_count");

            b.Property<DateTimeOffset>("OpenedAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("opened_at");

            b.Property<string>("OrderNumber")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("order_number");

            b.Property<decimal>("PaidAmount")
                .HasPrecision(18, 2)
                .HasColumnType("numeric(18,2)")
                .HasColumnName("paid_amount");

            b.Property<DateTimeOffset?>("PaidAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("paid_at");

            b.Property<string>("RestaurantId")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("restaurant_id");

            b.Property<string>("SessionId")
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("session_id");

            b.Property<string>("Status")
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnType("character varying(50)")
                .HasColumnName("status");

            b.Property<string>("TableId")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("table_id");

            b.Property<decimal>("TotalAmount")
                .HasPrecision(18, 2)
                .HasColumnType("numeric(18,2)")
                .HasColumnName("total_amount");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at");

            b.Property<long>("Version")
                .IsConcurrencyToken()
                .HasColumnType("bigint")
                .HasColumnName("version");

            b.Property<double?>("VideoPositionSeconds")
                .HasColumnType("double precision")
                .HasColumnName("video_position_seconds");

            b.Property<string>("WaiterId")
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("waiter_id");

            b.Property<string>("WaiterName")
                .HasMaxLength(150)
                .HasColumnType("character varying(150)")
                .HasColumnName("waiter_name");

            b.HasKey("OrderId");

            b.HasIndex("SessionId");

            b.HasIndex("TableId");

            b.ToTable("orders", "orders");
        });

        modelBuilder.Entity("FoodVision.Orders.Domain.OrderItem", b =>
        {
            b.Property<string>("OrderId")
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("order_id");

            b.Property<string>("ItemId")
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("item_id");

            b.Property<string>("Category")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("character varying(100)")
                .HasColumnName("category");

            b.Property<int>("Course")
                .HasColumnType("integer")
                .HasColumnName("course");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at");

            b.Property<string>("DishName")
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnType("character varying(200)")
                .HasColumnName("dish_name");

            b.Property<decimal>("Price")
                .HasPrecision(18, 2)
                .HasColumnType("numeric(18,2)")
                .HasColumnName("price");

            b.Property<int>("Quantity")
                .HasColumnType("integer")
                .HasColumnName("quantity");

            b.Property<DateTimeOffset?>("ServedAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("served_at");

            b.Property<string>("Status")
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnType("character varying(50)")
                .HasColumnName("status");

            b.Property<double?>("VideoPositionSeconds")
                .HasColumnType("double precision")
                .HasColumnName("video_position_seconds");

            b.HasKey("OrderId", "ItemId");

            b.ToTable("order_items", "orders");
        });

        modelBuilder.Entity("FoodVision.Orders.Domain.OrderItem", b =>
        {
            b.HasOne("FoodVision.Orders.Domain.Order", null)
                .WithMany("Items")
                .HasForeignKey("OrderId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("FoodVision.Orders.Domain.Order", b =>
        {
            b.Navigation("Items");
        });
    }
}
