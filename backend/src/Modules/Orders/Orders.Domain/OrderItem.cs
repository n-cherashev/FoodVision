using CSharpFunctionalExtensions;

namespace FoodVision.Orders.Domain;

public sealed class OrderItem
{
    public string ItemId { get; private set; } = null!;
    public string DishName { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public int Quantity { get; private set; }
    public decimal Price { get; private set; }
    public int Course { get; private set; }
    public ItemPreparationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ServedAt { get; private set; }
    public double? VideoPositionSeconds { get; private set; }

    private OrderItem() { }

    private OrderItem(
        string itemId,
        string dishName,
        string category,
        int quantity,
        decimal price,
        int course,
        ItemPreparationStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? servedAt,
        double? videoPositionSeconds)
    {
        ItemId = itemId;
        DishName = dishName;
        Category = category;
        Quantity = quantity;
        Price = price;
        Course = course;
        Status = status;
        CreatedAt = createdAt;
        ServedAt = servedAt;
        VideoPositionSeconds = videoPositionSeconds;
    }

    public static Result<OrderItem, string> Create(
        string itemId,
        string dishName,
        string category,
        int quantity,
        decimal price,
        int course = 1,
        double? videoPositionSeconds = null,
        DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return Result.Failure<OrderItem, string>("Идентификатор позиции не может быть пустым.");
        if (string.IsNullOrWhiteSpace(dishName))
            return Result.Failure<OrderItem, string>("Название блюда не может быть пустым.");
        if (quantity <= 0)
            return Result.Failure<OrderItem, string>("Количество должно быть больше нуля.");
        if (price < 0)
            return Result.Failure<OrderItem, string>("Цена не может быть отрицательной.");
        if (course <= 0)
            return Result.Failure<OrderItem, string>("Курс подачи должен быть положительным числом.");
        if (videoPositionSeconds.HasValue && (!double.IsFinite(videoPositionSeconds.Value) || videoPositionSeconds.Value < 0))
            return Result.Failure<OrderItem, string>("Позиция видео должна быть неотрицательным конечным числом.");

        return Result.Success<OrderItem, string>(new OrderItem(
            itemId.Trim(),
            dishName.Trim(),
            (category ?? "Основное").Trim(),
            quantity,
            price,
            course,
            ItemPreparationStatus.Ordered,
            createdAt ?? DateTimeOffset.UtcNow,
            null,
            videoPositionSeconds));
    }

    public UnitResult<string> MarkServed(DateTimeOffset servedAt, double? videoPositionSeconds = null)
    {
        Status = ItemPreparationStatus.Served;
        ServedAt = servedAt;
        if (videoPositionSeconds.HasValue)
            VideoPositionSeconds = videoPositionSeconds.Value;
        return UnitResult.Success<string>();
    }

    public void UpdateStatus(ItemPreparationStatus status)
    {
        Status = status;
    }
}
