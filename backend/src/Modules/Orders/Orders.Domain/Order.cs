using CSharpFunctionalExtensions;
using FoodVision.Orders.Contracts;

namespace FoodVision.Orders.Domain;

public sealed class Order
{
    private readonly List<OrderItem> _items = new();

    public string OrderId { get; private set; } = null!;
    public string OrderNumber { get; private set; } = null!;
    public string RestaurantId { get; private set; } = null!;
    public string TableId { get; private set; } = null!;
    public string? SessionId { get; private set; }
    public string? WaiterId { get; private set; }
    public string? WaiterName { get; private set; }
    public int GuestsCount { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public decimal PaidAmount { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? FirstDishServedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public double? VideoPositionSeconds { get; private set; }
    public double? PaidVideoPositionSeconds { get; private set; }
    public double? ClosedVideoPositionSeconds { get; private set; }

    public long Version { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();
    public bool IsActive => Status == OrderStatus.Open || Status == OrderStatus.Paid;

    private Order() { }

    private Order(
        string orderId,
        string orderNumber,
        string restaurantId,
        string tableId,
        string? sessionId,
        string? waiterId,
        string? waiterName,
        int guestsCount,
        double? videoPositionSeconds,
        DateTimeOffset openedAt)
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        RestaurantId = restaurantId;
        TableId = tableId;
        SessionId = sessionId;
        WaiterId = waiterId;
        WaiterName = waiterName;
        GuestsCount = guestsCount;
        Status = OrderStatus.Open;
        TotalAmount = 0;
        PaidAmount = 0;
        OpenedAt = openedAt;
        VideoPositionSeconds = videoPositionSeconds;
        Version = 1;
        UpdatedAt = openedAt;
    }

    public static Result<Order, string> Create(
        string orderId,
        string tableId,
        string restaurantId = "default",
        string? orderNumber = null,
        string? sessionId = null,
        string? waiterId = null,
        string? waiterName = null,
        int guestsCount = 1,
        double? videoPositionSeconds = null,
        DateTimeOffset? openedAt = null)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return Result.Failure<Order, string>("Идентификатор заказа не может быть пустым.");
        if (string.IsNullOrWhiteSpace(tableId))
            return Result.Failure<Order, string>("Идентификатор стола не может быть пустым.");
        if (string.IsNullOrWhiteSpace(restaurantId))
            return Result.Failure<Order, string>("Идентификатор ресторана не может быть пустым.");
        if (guestsCount <= 0)
            return Result.Failure<Order, string>("Количество гостей должно быть больше нуля.");
        if (videoPositionSeconds.HasValue && (!double.IsFinite(videoPositionSeconds.Value) || videoPositionSeconds.Value < 0))
            return Result.Failure<Order, string>("Позиция видео должна быть неотрицательным конечным числом.");

        var timestamp = openedAt ?? DateTimeOffset.UtcNow;
        var number = string.IsNullOrWhiteSpace(orderNumber) ? $"№{orderId.Trim()}" : orderNumber.Trim();

        return Result.Success<Order, string>(new Order(
            orderId.Trim(),
            number,
            restaurantId.Trim(),
            tableId.Trim(),
            sessionId?.Trim(),
            waiterId?.Trim(),
            waiterName?.Trim(),
            guestsCount,
            videoPositionSeconds,
            timestamp));
    }

    public UnitResult<string> AddItem(
        string itemId,
        string dishName,
        string category,
        int quantity,
        decimal price,
        int course = 1,
        double? videoPositionSeconds = null,
        DateTimeOffset? timestamp = null)
    {
        if (Status == OrderStatus.Closed || Status == OrderStatus.Cancelled)
            return UnitResult.Failure<string>("Нельзя добавлять позиции в закрытый или отменённый заказ.");

        var itemResult = OrderItem.Create(itemId, dishName, category, quantity, price, course, videoPositionSeconds, timestamp);
        if (itemResult.IsFailure)
            return UnitResult.Failure<string>(itemResult.Error);

        _items.Add(itemResult.Value);
        RecalculateTotal();
        UpdatedAt = timestamp ?? DateTimeOffset.UtcNow;
        Version++;
        return UnitResult.Success<string>();
    }

    public UnitResult<string> MarkItemServed(
        string itemId,
        DateTimeOffset servedAt,
        double? videoPositionSeconds = null)
    {
        var item = _items.FirstOrDefault(i => i.ItemId == itemId);
        if (item == null)
            return UnitResult.Failure<string>($"Позиция с идентификатором '{itemId}' не найдена в заказе.");

        item.MarkServed(servedAt, videoPositionSeconds);

        if (!FirstDishServedAt.HasValue)
        {
            FirstDishServedAt = servedAt;
        }

        UpdatedAt = servedAt;
        Version++;
        return UnitResult.Success<string>();
    }

    public UnitResult<string> Pay(
        decimal? amount = null,
        DateTimeOffset? paidAt = null,
        double? videoPositionSeconds = null)
    {
        if (Status != OrderStatus.Open && Status != OrderStatus.Paid)
            return UnitResult.Failure<string>("Оплатить можно только открытый заказ.");

        var timestamp = paidAt ?? DateTimeOffset.UtcNow;
        Status = OrderStatus.Paid;
        PaidAt = timestamp;
        PaidAmount = amount ?? TotalAmount;
        if (videoPositionSeconds.HasValue)
            PaidVideoPositionSeconds = videoPositionSeconds.Value;

        UpdatedAt = timestamp;
        Version++;
        return UnitResult.Success<string>();
    }

    public UnitResult<string> Close(
        DateTimeOffset? closedAt = null,
        double? videoPositionSeconds = null)
    {
        if (Status == OrderStatus.Closed)
            return UnitResult.Success<string>();
        if (Status == OrderStatus.Cancelled)
            return UnitResult.Failure<string>("Нельзя закрыть отменённый заказ.");

        var timestamp = closedAt ?? DateTimeOffset.UtcNow;
        Status = OrderStatus.Closed;
        ClosedAt = timestamp;
        if (videoPositionSeconds.HasValue)
            ClosedVideoPositionSeconds = videoPositionSeconds.Value;

        UpdatedAt = timestamp;
        Version++;
        return UnitResult.Success<string>();
    }

    public UnitResult<string> Cancel(
        string reason,
        DateTimeOffset? cancelledAt = null,
        double? videoPositionSeconds = null)
    {
        if (Status == OrderStatus.Closed)
            return UnitResult.Failure<string>("Нельзя отменить уже закрытый заказ.");

        var timestamp = cancelledAt ?? DateTimeOffset.UtcNow;
        Status = OrderStatus.Cancelled;
        CancelledAt = timestamp;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Отменён" : reason.Trim();

        UpdatedAt = timestamp;
        Version++;
        return UnitResult.Success<string>();
    }

    public UnitResult<string> Transfer(
        string newTableId,
        DateTimeOffset? transferredAt = null,
        double? videoPositionSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(newTableId))
            return UnitResult.Failure<string>("Идентификатор нового стола не может быть пустым.");
        if (string.Equals(TableId, newTableId.Trim(), StringComparison.OrdinalIgnoreCase))
            return UnitResult.Failure<string>("Новый стол совпадает с текущим столом заказа.");
        if (Status == OrderStatus.Closed || Status == OrderStatus.Cancelled)
            return UnitResult.Failure<string>("Нельзя перенести закрытый или отменённый заказ.");

        var timestamp = transferredAt ?? DateTimeOffset.UtcNow;
        TableId = newTableId.Trim();

        UpdatedAt = timestamp;
        Version++;
        return UnitResult.Success<string>();
    }

    public OrderEvent ToEvent(OrderEventType eventType, string? targetTableId = null, double? videoPositionSeconds = null)
    {
        return new OrderEvent(
            Guid.NewGuid(),
            Version,
            eventType,
            OrderId,
            TableId,
            RestaurantId,
            targetTableId,
            Status,
            UpdatedAt,
            videoPositionSeconds ?? VideoPositionSeconds,
            OpenedAt,
            PaidAt,
            ClosedAt);
    }

    public OrderDto ToDto()
    {
        return new OrderDto(
            OrderId,
            TableId,
            RestaurantId,
            Status,
            OpenedAt,
            PaidAt,
            ClosedAt,
            UpdatedAt,
            Version);
    }

    private void RecalculateTotal()
    {
        TotalAmount = _items.Sum(i => i.Price * i.Quantity);
    }
}
