using CSharpFunctionalExtensions;
using FoodVision.Orders.Contracts;

namespace FoodVision.Tables.Domain;

public sealed class TableOrderInfo
{
    public string OrderId { get; private set; } = null!;
    public OrderStatus Status { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public long Version { get; private set; }

    public bool IsOpen => Status == OrderStatus.Open || Status == OrderStatus.Paid;

    private TableOrderInfo() { }

    private TableOrderInfo(string orderId, OrderStatus status, DateTimeOffset openedAt, DateTimeOffset? paidAt, long version)
    {
        OrderId = orderId;
        Status = status;
        OpenedAt = openedAt;
        PaidAt = paidAt;
        Version = version;
    }

    public static Result<TableOrderInfo, string> Create(
        string orderId,
        OrderStatus status,
        DateTimeOffset openedAt,
        DateTimeOffset? paidAt = null,
        long version = 1)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return Result.Failure<TableOrderInfo, string>("Идентификатор заказа не может быть пустым.");
        if (version < 1)
            return Result.Failure<TableOrderInfo, string>("Версия заказа должна быть положительным числом.");

        return Result.Success<TableOrderInfo, string>(new TableOrderInfo(orderId.Trim(), status, openedAt, paidAt, version));
    }

    public void MarkPaid(DateTimeOffset paidAt, long version)
    {
        Status = OrderStatus.Paid;
        PaidAt = paidAt;
        if (version > Version) Version = version;
    }

    public void UpdateStatus(OrderStatus status, DateTimeOffset? paidAt, long version)
    {
        Status = status;
        if (paidAt.HasValue) PaidAt = paidAt.Value;
        if (version > Version) Version = version;
    }
}
