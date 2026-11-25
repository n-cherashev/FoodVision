using FoodVision.Orders.Domain;
using FoodVision.Orders.Infrastructure.Postgres;
using FoodVision.Orders.Infrastructure.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FoodVision.Checks;

public static class OrderPostgresChecks
{
    public static async Task Run(string? connectionString = null)
    {
        connectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__FoodVision")
            ?? Environment.GetEnvironmentVariable("FOODVISION_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=foodvision;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        try
        {
            await using var testConn = new NpgsqlConnection(connectionString);
            await testConn.OpenAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PostgreSQL is not accessible ({ex.Message}). Skipping Orders PostgreSQL checks.");
            return;
        }

        // 1. Run migrations
        await using (var db = new OrdersDbContext(options))
        {
            var migrator = new OrdersDatabaseMigrator(db);
            await migrator.MigrateAsync();
        }

        var orderId = $"ord-{Guid.NewGuid():N}";
        var tableId = $"table-{Guid.NewGuid():N}";
        var sessionId = $"sess-{Guid.NewGuid():N}";

        // 2. Save order with items
        await using (var db = new OrdersDbContext(options))
        {
            var repo = new OrdersRepository(db);
            var order = Order.Create(orderId, tableId, "rest-1", "№101", sessionId, "waiter-1", "Иван", 2, 45.0).Value;
            order.AddItem("i-1", "Борщ", "Супы", 2, 350, 1, 45.0);
            order.AddItem("i-2", "Чай зелёный", "Напитки", 1, 200, 1, 45.0);

            var saveResult = await repo.Save(order);
            if (saveResult.IsFailure)
                throw new Exception($"Saving order to Postgres failed: {saveResult.Error}");
        }

        // 3. Reload and verify items and totals
        await using (var db = new OrdersDbContext(options))
        {
            var repo = new OrdersRepository(db);
            var loaded = await repo.GetById(orderId);
            if (loaded.IsFailure)
                throw new Exception("Loading saved order failed.");

            var order = loaded.Value;
            if (order.TableId != tableId || order.SessionId != sessionId || order.Items.Count != 2 || order.TotalAmount != 900)
                throw new Exception($"Loaded order fields or total amount mismatch: expected 900, got {order.TotalAmount}");
        }

        // 4. Concurrency check
        await using (var dbA = new OrdersDbContext(options))
        await using (var dbB = new OrdersDbContext(options))
        {
            var repoA = new OrdersRepository(dbA);
            var repoB = new OrdersRepository(dbB);

            var orderA = (await repoA.GetById(orderId)).Value;
            var orderB = (await repoB.GetById(orderId)).Value;

            orderA.Pay(900, DateTimeOffset.UtcNow, 120.0);
            var saveA = await repoA.Save(orderA);
            if (saveA.IsFailure)
                throw new Exception("Context A save must succeed.");

            orderB.Cancel("Отмена", DateTimeOffset.UtcNow, 125.0);
            var saveB = await repoB.Save(orderB);

            if (saveB.IsSuccess)
                throw new Exception("Context B must fail with concurrency conflict on stale order!");
        }

        // 5. Query by session and table
        await using (var db = new OrdersDbContext(options))
        {
            var repo = new OrdersRepository(db);
            var tableOrders = await repo.GetByTable(tableId);
            if (tableOrders.Count != 1 || tableOrders[0].OrderId != orderId)
                throw new Exception("Querying orders by table failed.");

            var sessionOrders = await repo.GetBySession(sessionId);
            if (sessionOrders.Count != 1 || sessionOrders[0].OrderId != orderId)
                throw new Exception("Querying orders by session failed.");
        }

        Console.WriteLine("Orders PostgreSQL integration checks passed.");
    }
}
