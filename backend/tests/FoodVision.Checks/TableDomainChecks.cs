using System.Reflection;
using FoodVision.Orders.Contracts;
using FoodVision.Tables.Domain;

namespace FoodVision.Checks;

public static class TableDomainChecks
{
    public static void Run()
    {
        CheckEncapsulationAndImmutability();
        CheckCreationValidation();
        CheckOccupancyTruthTable();
        CheckOrderPaymentKeepsOrderOpen();
        CheckMultipleOrdersLifecycle();
        CheckOrderTransferRecalculatesBothTables();
        CheckCleaningConfirmationRulesAndIdempotency();
        CheckMissingAndStaleDataResilience();
        Console.WriteLine("Table domain scenarios passed.");
    }

    private static void CheckEncapsulationAndImmutability()
    {
        // TableSessionState constructors must be non-public
        var sessionStateConstructors = typeof(TableSessionState).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (sessionStateConstructors.Length > 0)
            throw new Exception("TableSessionState must not have public constructors. Use TableSessionState.Create.");

        // TableSessionState properties must not have public setters
        var publicSetters = typeof(TableSessionState).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(false) != null)
            .ToList();
        if (publicSetters.Count > 0)
            throw new Exception($"TableSessionState must not have public setters. Found: {string.Join(", ", publicSetters.Select(p => p.Name))}");

        // TableOrderInfo constructors and setters must be non-public
        var orderInfoConstructors = typeof(TableOrderInfo).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (orderInfoConstructors.Length > 0)
            throw new Exception("TableOrderInfo must not have public constructors. Use TableOrderInfo.Create.");
        var orderPublicSetters = typeof(TableOrderInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(false) != null)
            .ToList();
        if (orderPublicSetters.Count > 0)
            throw new Exception($"TableOrderInfo must not have public setters. Found: {string.Join(", ", orderPublicSetters.Select(p => p.Name))}");

        // TableEvent constructors and setters must be non-public
        var eventConstructors = typeof(TableEvent).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (eventConstructors.Length > 0)
            throw new Exception("TableEvent must not have public constructors. Use TableEvent.Create.");
        var eventPublicSetters = typeof(TableEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(false) != null)
            .ToList();
        if (eventPublicSetters.Count > 0)
            throw new Exception($"TableEvent must not have public setters. Found: {string.Join(", ", eventPublicSetters.Select(p => p.Name))}");
    }

    private static void CheckCreationValidation()
    {
        if (TableSessionState.Create("", "table-1").IsSuccess)
            throw new Exception("Empty sessionId must be rejected.");
        if (TableSessionState.Create("session-1", "").IsSuccess)
            throw new Exception("Empty tableId must be rejected.");
        if (TableSessionState.Create("session-1", "table-1", "").IsSuccess)
            throw new Exception("Empty restaurantId must be rejected.");

        if (TableOrderInfo.Create("", OrderStatus.Open, DateTimeOffset.UtcNow).IsSuccess)
            throw new Exception("Empty orderId in TableOrderInfo must be rejected.");
        if (TableOrderInfo.Create("order-1", OrderStatus.Open, DateTimeOffset.UtcNow, version: 0).IsSuccess)
            throw new Exception("Non-positive version in TableOrderInfo must be rejected.");

        if (TableEvent.Create(Guid.Empty, "s", "t", TableEventType.PeopleArrived, TableEventSource.VideoAnalytics, 0, DateTimeOffset.UtcNow, "desc").IsSuccess)
            throw new Exception("Empty event Id must be rejected.");
        if (TableEvent.Create(Guid.NewGuid(), "", "t", TableEventType.PeopleArrived, TableEventSource.VideoAnalytics, 0, DateTimeOffset.UtcNow, "desc").IsSuccess)
            throw new Exception("Empty sessionId in TableEvent must be rejected.");
        if (TableEvent.Create(Guid.NewGuid(), "s", "", TableEventType.PeopleArrived, TableEventSource.VideoAnalytics, 0, DateTimeOffset.UtcNow, "desc").IsSuccess)
            throw new Exception("Empty tableId in TableEvent must be rejected.");
        if (TableEvent.Create(Guid.NewGuid(), "s", "t", TableEventType.PeopleArrived, TableEventSource.VideoAnalytics, -5, DateTimeOffset.UtcNow, "desc").IsSuccess)
            throw new Exception("Negative video position in TableEvent must be rejected.");

        var valid = TableSessionState.Create("s1", "t1");
        if (valid.IsFailure || valid.Value.IsOccupied || valid.Value.CleaningStatus != TableCleaningStatus.Unknown)
            throw new Exception("Initial table state is invalid.");
    }

    private static void CheckOccupancyTruthTable()
    {
        // Truth table according to ТЗ:
        // People | Items | OpenOrder | Expected Occupancy
        // Yes    | Yes   | Yes       | Occupied
        // Yes    | Yes   | No        | Occupied
        // Yes    | No    | Yes       | Occupied
        // Yes    | No    | No        | Occupied
        // No     | Yes   | Yes       | Occupied
        // No     | Yes   | No        | Occupied
        // No     | No    | Yes       | Occupied
        // No     | No    | No        | Not Occupied
        var combinations = new (bool People, bool Items, bool Order, bool ExpectedOccupied)[]
        {
            (true, true, true, true),
            (true, true, false, true),
            (true, false, true, true),
            (true, false, false, true),
            (false, true, true, true),
            (false, true, false, true),
            (false, false, true, true),
            (false, false, false, false),
        };

        var now = DateTimeOffset.UtcNow;
        foreach (var combo in combinations)
        {
            var state = TableSessionState.Create("sess", "table").Value;

            if (combo.Order)
            {
                var ev = new OrderEvent(Guid.NewGuid(), 1, OrderEventType.Opened, "ord-1", "table", "rest", null,
                    OrderStatus.Open, now, 0);
                state.ApplyOrderEvent(ev);
            }

            state.ApplyVideoObservation(
                hasPeople: combo.People,
                peopleCount: combo.People ? 2 : 0,
                hasBelongings: combo.Items,
                freshness: DataFreshness.Fresh,
                videoPositionSeconds: 1.0,
                timestamp: now);

            if (state.IsOccupied != combo.ExpectedOccupied)
                throw new Exception($"Truth table mismatch for (People: {combo.People}, Items: {combo.Items}, Order: {combo.Order}): expected {combo.ExpectedOccupied}, got {state.IsOccupied}");
        }
    }

    private static void CheckOrderPaymentKeepsOrderOpen()
    {
        var now = DateTimeOffset.UtcNow;
        var state = TableSessionState.Create("sess", "table").Value;

        // Open order
        state.ApplyOrderEvent(new OrderEvent(Guid.NewGuid(), 1, OrderEventType.Opened, "ord-1", "table", "rest", null,
            OrderStatus.Open, now, 10));
        if (!state.IsOccupied || !state.HasOpenOrders)
            throw new Exception("Table must be occupied when order is opened.");

        // Pay order: PAYMENT DOES NOT CLOSE ORDER!
        state.ApplyOrderEvent(new OrderEvent(Guid.NewGuid(), 2, OrderEventType.Paid, "ord-1", "table", "rest", null,
            OrderStatus.Paid, now.AddMinutes(5), 300, PaidAt: now.AddMinutes(5)));

        if (!state.IsOccupied)
            throw new Exception("Table must remain occupied after payment without closing.");
        if (!state.HasOpenOrders)
            throw new Exception("Order must remain open/active after payment without closing.");

        // Close order: now order is closed and table freed
        state.ApplyOrderEvent(new OrderEvent(Guid.NewGuid(), 3, OrderEventType.Closed, "ord-1", "table", "rest", null,
            OrderStatus.Closed, now.AddMinutes(10), 600, ClosedAt: now.AddMinutes(10)));

        if (state.IsOccupied || state.HasOpenOrders)
            throw new Exception("Table must be unoccupied after order is closed.");
        if (state.CleaningStatus != TableCleaningStatus.NeedsCleaning)
            throw new Exception("Used table must transition to NeedsCleaning after being freed.");
    }

    private static void CheckMultipleOrdersLifecycle()
    {
        var now = DateTimeOffset.UtcNow;
        var state = TableSessionState.Create("sess", "table").Value;

        // Two orders on the same table
        state.ApplyOrderEvent(new OrderEvent(Guid.NewGuid(), 1, OrderEventType.Opened, "ord-1", "table", "rest", null,
            OrderStatus.Open, now, 0));
        state.ApplyOrderEvent(new OrderEvent(Guid.NewGuid(), 2, OrderEventType.Opened, "ord-2", "table", "rest", null,
            OrderStatus.Open, now, 10));

        if (state.Orders.Count != 2 || !state.IsOccupied)
            throw new Exception("Table must hold two active orders.");

        // Close first order: table must remain occupied because ord-2 is still open!
        state.ApplyOrderEvent(new OrderEvent(Guid.NewGuid(), 3, OrderEventType.Closed, "ord-1", "table", "rest", null,
            OrderStatus.Closed, now.AddMinutes(10), 100));

        if (!state.IsOccupied || !state.HasOpenOrders || state.Orders.Count != 1)
            throw new Exception("Table must remain occupied until all orders are closed or cancelled.");

        // Cancel second order: now table is freed
        state.ApplyOrderEvent(new OrderEvent(Guid.NewGuid(), 4, OrderEventType.Cancelled, "ord-2", "table", "rest", null,
            OrderStatus.Cancelled, now.AddMinutes(15), 200));

        if (state.IsOccupied || state.HasOpenOrders || state.Orders.Count != 0)
            throw new Exception("Table must be free after last order is cancelled.");
        if (state.CleaningStatus != TableCleaningStatus.NeedsCleaning)
            throw new Exception("Table must transition to NeedsCleaning.");
    }

    private static void CheckOrderTransferRecalculatesBothTables()
    {
        var now = DateTimeOffset.UtcNow;
        var tableA = TableSessionState.Create("sess", "table-A").Value;
        var tableB = TableSessionState.Create("sess", "table-B").Value;

        // Open two orders on table A
        tableA.ApplyOrderEvent(new OrderEvent(Guid.NewGuid(), 1, OrderEventType.Opened, "ord-1", "table-A", "rest", null,
            OrderStatus.Open, now, 0));
        tableA.ApplyOrderEvent(new OrderEvent(Guid.NewGuid(), 2, OrderEventType.Opened, "ord-2", "table-A", "rest", null,
            OrderStatus.Open, now, 0));

        if (!tableA.IsOccupied || tableB.IsOccupied)
            throw new Exception("Table A should be occupied, Table B free.");

        // Transfer order 1 from Table A to Table B
        var transfer1 = new OrderEvent(Guid.NewGuid(), 3, OrderEventType.Transferred, "ord-1", "table-A", "rest", "table-B",
            OrderStatus.Open, now.AddMinutes(5), 50);

        tableA.ApplyOrderEvent(transfer1);
        tableB.ApplyOrderEvent(transfer1);

        // Table A has ord-2 remaining -> remains occupied
        if (!tableA.IsOccupied || tableA.Orders.Count != 1)
            throw new Exception("Table A must remain occupied with remaining order after partial transfer.");
        // Table B has ord-1 -> becomes occupied
        if (!tableB.IsOccupied || tableB.Orders.Count != 1)
            throw new Exception("Table B must become occupied after receiving transferred order.");

        // Transfer order 2 from Table A to Table B
        var transfer2 = new OrderEvent(Guid.NewGuid(), 4, OrderEventType.Transferred, "ord-2", "table-A", "rest", "table-B",
            OrderStatus.Open, now.AddMinutes(10), 100);

        tableA.ApplyOrderEvent(transfer2);
        tableB.ApplyOrderEvent(transfer2);

        // Table A now has 0 orders -> becomes unoccupied and requires cleaning
        if (tableA.IsOccupied || tableA.Orders.Count != 0 || tableA.CleaningStatus != TableCleaningStatus.NeedsCleaning)
            throw new Exception("Table A must become free and require cleaning after all orders transferred.");
        // Table B now has both orders
        if (!tableB.IsOccupied || tableB.Orders.Count != 2)
            throw new Exception("Table B must have both orders and remain occupied.");
    }

    private static void CheckCleaningConfirmationRulesAndIdempotency()
    {
        var now = DateTimeOffset.UtcNow;
        var state = TableSessionState.Create("sess", "table").Value;

        // 1. Initial state: data freshness is NoData. Confirming cleaning must fail.
        var failNoData = state.ConfirmCleaning(0, now);
        if (failNoData.IsSuccess || !failNoData.Error.Contains("видеоаналитики"))
            throw new Exception("Cleaning confirmation must fail when video data is missing.");

        // 2. Simulate video fresh, but table is occupied
        state.ApplyVideoObservation(true, 2, false, DataFreshness.Fresh, 10, now);
        var failOccupied = state.ConfirmCleaning(10, now);
        if (failOccupied.IsSuccess || !failOccupied.Error.Contains("занят"))
            throw new Exception("Cleaning confirmation must fail when table is occupied.");

        // 3. Guests leave, table requires cleaning
        state.ApplyVideoObservation(false, 0, false, DataFreshness.Fresh, 20, now.AddMinutes(1));
        if (state.CleaningStatus != TableCleaningStatus.NeedsCleaning)
            throw new Exception("Table must require cleaning after guests leave.");

        // Orders freshness is still NoData! Confirming cleaning must fail.
        var failOrdersNoData = state.ConfirmCleaning(20, now.AddMinutes(1));
        if (failOrdersNoData.IsSuccess || !failOrdersNoData.Error.Contains("заказах"))
            throw new Exception("Cleaning confirmation must fail when order data freshness is missing.");

        // 4. Update orders freshness to Fresh
        state.ApplyOrderSnapshot(new OrdersSnapshot("rest", [], now.AddMinutes(1), 1), DataFreshness.Fresh, now.AddMinutes(1));

        // 5. Now both sources are Fresh and table is free: ConfirmCleaning must succeed!
        var successConfirm = state.ConfirmCleaning(25, now.AddMinutes(2), actionId: "clean-act-1");
        if (successConfirm.IsFailure)
            throw new Exception($"Cleaning confirmation must succeed when conditions met: {successConfirm.Error}");
        if (state.CleaningStatus != TableCleaningStatus.Ready)
            throw new Exception("Table status must be Ready after confirmed cleaning.");

        var events = state.UncommittedEvents.ToList();
        var cleanEvent = events.FirstOrDefault(e => e.EventType == TableEventType.CleaningConfirmed);
        if (cleanEvent == null || cleanEvent.Source != TableEventSource.Manual || cleanEvent.ActionId != "clean-act-1")
            throw new Exception("CleaningConfirmed event must be recorded with source Manual and actionId.");

        // 6. Idempotency: repeated call with same actionId must succeed without duplicate event
        var eventCountBefore = state.UncommittedEvents.Count;
        var repeatedConfirm = state.ConfirmCleaning(25, now.AddMinutes(2), actionId: "clean-act-1");
        if (repeatedConfirm.IsFailure || state.UncommittedEvents.Count != eventCountBefore)
            throw new Exception("Repeated cleaning confirmation with same actionId must be idempotent and not create duplicate events.");
    }

    private static void CheckMissingAndStaleDataResilience()
    {
        var now = DateTimeOffset.UtcNow;
        var state = TableSessionState.Create("sess", "table").Value;

        // Seated guests
        state.ApplyVideoObservation(true, 3, false, DataFreshness.Fresh, 10, now);
        if (!state.IsOccupied || !state.HasPeople || state.PeopleCount != 3)
            throw new Exception("Guests must be seated.");

        state.ClearUncommittedEvents();

        // Observation becomes Stale (loss of visibility or lag)
        state.ApplyVideoObservation(null, null, null, DataFreshness.Stale, 15, now.AddSeconds(5));

        // CRITICAL: Stale data must NOT cause departure event or clear guests!
        if (!state.HasPeople || state.PeopleCount != 3 || !state.IsOccupied)
            throw new Exception("Stale observation must not clear guests or occupancy!");

        var departureEvents = state.UncommittedEvents.Where(e => e.EventType == TableEventType.PeopleLeft).ToList();
        if (departureEvents.Count > 0)
            throw new Exception("Stale observation must not generate PeopleLeft events!");

        // Stale video must prevent cleaning confirmation
        var confirmResult = state.ConfirmCleaning(15, now.AddSeconds(5));
        if (confirmResult.IsSuccess)
            throw new Exception("Cleaning cannot be confirmed while data is stale.");
    }
}
