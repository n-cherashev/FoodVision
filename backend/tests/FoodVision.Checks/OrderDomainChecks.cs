using System.Reflection;
using FoodVision.Orders.Contracts;
using FoodVision.Orders.Domain;

namespace FoodVision.Checks;

public static class OrderDomainChecks
{
    public static void Run()
    {
        CheckEncapsulationAndImmutability();
        CheckCreationValidation();
        CheckItemsAndServingCalculation();
        CheckPaymentAndLifecycle();
        CheckTransferAndCancellation();
        Console.WriteLine("Order domain scenarios passed.");
    }

    private static void CheckEncapsulationAndImmutability()
    {
        // Order constructors must be non-public
        var orderConstructors = typeof(Order).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (orderConstructors.Length > 0)
            throw new Exception("Order must not have public constructors. Use Order.Create.");

        // Order properties must not have public setters
        var publicSetters = typeof(Order).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(false) != null)
            .ToList();
        if (publicSetters.Count > 0)
            throw new Exception($"Order must not have public setters: {string.Join(", ", publicSetters.Select(p => p.Name))}");

        // OrderItem constructors and setters
        var itemConstructors = typeof(OrderItem).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (itemConstructors.Length > 0)
            throw new Exception("OrderItem must not have public constructors. Use OrderItem.Create.");

        var itemPublicSetters = typeof(OrderItem).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(false) != null)
            .ToList();
        if (itemPublicSetters.Count > 0)
            throw new Exception($"OrderItem must not have public setters: {string.Join(", ", itemPublicSetters.Select(p => p.Name))}");
    }

    private static void CheckCreationValidation()
    {
        if (Order.Create("", "table-1").IsSuccess)
            throw new Exception("Empty orderId must be rejected.");
        if (Order.Create("ord-1", "").IsSuccess)
            throw new Exception("Empty tableId must be rejected.");
        if (Order.Create("ord-1", "t1", "").IsSuccess)
            throw new Exception("Empty restaurantId must be rejected.");
        if (Order.Create("ord-1", "t1", guestsCount: 0).IsSuccess)
            throw new Exception("Non-positive guestsCount must be rejected.");
        if (Order.Create("ord-1", "t1", videoPositionSeconds: -1).IsSuccess)
            throw new Exception("Negative video position must be rejected.");

        var valid = Order.Create("ord-1", "table-1", guestsCount: 3).Value;
        if (valid.Status != OrderStatus.Open || valid.TotalAmount != 0 || valid.Version != 1)
            throw new Exception("Initial order state is invalid.");
    }

    private static void CheckItemsAndServingCalculation()
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create("ord-1", "table-1").Value;

        // Add 2 dishes
        var add1 = order.AddItem("item-1", "Том Ям", "Супы", 2, 450, 1, 10, now);
        var add2 = order.AddItem("item-2", "Стейк Рибай", "Горячее", 1, 1200, 2, 15, now);

        if (add1.IsFailure || add2.IsFailure)
            throw new Exception("Adding items must succeed.");

        if (order.Items.Count != 2 || order.TotalAmount != (2 * 450 + 1 * 1200))
            throw new Exception($"TotalAmount calculation incorrect: expected 2100, got {order.TotalAmount}");

        if (order.FirstDishServedAt.HasValue)
            throw new Exception("FirstDishServedAt must be null before any dish is served.");

        // Serve first item
        var serveTime1 = now.AddMinutes(12);
        var serveResult1 = order.MarkItemServed("item-1", serveTime1, 720);
        if (serveResult1.IsFailure || !order.FirstDishServedAt.HasValue || order.FirstDishServedAt.Value != serveTime1)
            throw new Exception("FirstDishServedAt must be recorded on first served item.");

        // Serve second item: FirstDishServedAt must NOT change
        var serveTime2 = now.AddMinutes(20);
        var serveResult2 = order.MarkItemServed("item-2", serveTime2, 1200);
        if (serveResult2.IsFailure || order.FirstDishServedAt.Value != serveTime1)
            throw new Exception("FirstDishServedAt must remain the timestamp of the first dish.");
    }

    private static void CheckPaymentAndLifecycle()
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create("ord-1", "table-1").Value;
        order.AddItem("item-1", "Кофе", "Напитки", 1, 200, 1);

        // Pay order
        var payResult = order.Pay(200, now.AddMinutes(5), 300);
        if (payResult.IsFailure || order.Status != OrderStatus.Paid || !order.IsActive)
            throw new Exception("Order must be Paid and remain active after payment.");

        // Close order
        var closeResult = order.Close(now.AddMinutes(10), 600);
        if (closeResult.IsFailure || order.Status != OrderStatus.Closed || order.IsActive)
            throw new Exception("Order must be Closed and inactive after closing.");

        // Cannot add items to closed order
        var addAfterClose = order.AddItem("item-2", "Десерт", "Десерты", 1, 300);
        if (addAfterClose.IsSuccess)
            throw new Exception("Adding items to closed order must be rejected.");
    }

    private static void CheckTransferAndCancellation()
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create("ord-transfer", "table-A").Value;

        // Transfer to table-B
        var transferResult = order.Transfer("table-B", now.AddMinutes(2));
        if (transferResult.IsFailure || order.TableId != "table-B")
            throw new Exception("Transfer must update tableId.");

        // Transfer to same table must fail
        var transferSame = order.Transfer("table-B");
        if (transferSame.IsSuccess)
            throw new Exception("Transfer to same table must fail.");

        // Cancel order
        var cancelResult = order.Cancel("Гости отказались от заказа", now.AddMinutes(5));
        if (cancelResult.IsFailure || order.Status != OrderStatus.Cancelled || order.CancellationReason != "Гости отказались от заказа")
            throw new Exception("Cancellation must set status and reason.");

        // Cannot close cancelled order
        var closeCancelled = order.Close();
        if (closeCancelled.IsSuccess)
            throw new Exception("Closing cancelled order must fail.");
    }
}
