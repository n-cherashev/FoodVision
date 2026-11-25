using FoodVision.Orders.Contracts;
using FoodVision.Tables.Domain;
using FoodVision.Tables.Infrastructure.Postgres;
using FoodVision.Tables.Infrastructure.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FoodVision.Checks;

public static class TablePostgresChecks
{
    public static async Task Run(string? connectionString = null)
    {
        connectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__FoodVision")
            ?? Environment.GetEnvironmentVariable("FOODVISION_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=foodvision;Username=postgres;Password=postgres";

        Console.WriteLine("Connecting to PostgreSQL for integration checks...");
        var options = new DbContextOptionsBuilder<TablesDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", TablesDbContext.Schema))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        try
        {
            await using var testConn = new NpgsqlConnection(connectionString);
            await testConn.OpenAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PostgreSQL is not accessible ({ex.Message}). Skipping PostgreSQL integration checks.");
            return;
        }

        // 1. Run migrations
        await using (var db = new TablesDbContext(options))
        {
            var migrator = new TablesDatabaseMigrator(db);
            await migrator.MigrateAsync();
        }

        var sessionId1 = $"session-{Guid.NewGuid():N}";
        var sessionId2 = $"session-{Guid.NewGuid():N}";
        var tableId = "table-1";

        // 2. Atomic save of state and events
        await using (var db = new TablesDbContext(options))
        {
            var repo = new TableSessionRepository(db);
            var state = TableSessionState.Create(sessionId1, tableId).Value;
            var now = DateTimeOffset.UtcNow;

            state.ApplyVideoObservation(true, 2, false, DataFreshness.Fresh, 5.0, now);
            state.ApplyOrderEvent(new OrderEvent(Guid.NewGuid(), 1, OrderEventType.Opened, "ord-100", tableId, "rest", null,
                OrderStatus.Open, now, 5.0));

            var saveResult = await repo.Save(state);
            if (saveResult.IsFailure)
                throw new Exception($"Atomic save failed: {saveResult.Error}");

            if (state.UncommittedEvents.Count != 0)
                throw new Exception("Uncommitted events should be cleared after save.");
        }

        // 3. Verify session isolation
        await using (var db = new TablesDbContext(options))
        {
            var repo = new TableSessionRepository(db);
            var stateSession2 = TableSessionState.Create(sessionId2, tableId).Value;
            var now = DateTimeOffset.UtcNow;

            stateSession2.ApplyVideoObservation(false, 0, false, DataFreshness.Fresh, 0.0, now);
            var saveResult = await repo.Save(stateSession2);
            if (saveResult.IsFailure)
                throw new Exception($"Save for session 2 failed: {saveResult.Error}");

            var loadSession1 = await repo.GetBy(sessionId1, tableId);
            var loadSession2 = await repo.GetBy(sessionId2, tableId);

            if (loadSession1.IsFailure || loadSession2.IsFailure)
                throw new Exception("Both sessions must be retrievable.");

            if (!loadSession1.Value.IsOccupied || loadSession1.Value.PeopleCount != 2)
                throw new Exception("Session 1 must remain occupied with 2 people.");

            if (loadSession2.Value.IsOccupied || loadSession2.Value.PeopleCount != 0)
                throw new Exception("Session 2 must be unoccupied.");

            var eventsSession1 = await repo.GetEvents(sessionId1, tableId);
            var eventsSession2 = await repo.GetEvents(sessionId2, tableId);

            if (eventsSession1.Count < 2 || eventsSession2.Count == 0)
                throw new Exception("Events must be isolated per session.");
            if (eventsSession1.Any(e => e.SessionId != sessionId1) || eventsSession2.Any(e => e.SessionId != sessionId2))
                throw new Exception("Session events are mixed across sessions.");
        }

        // 4. Verify reconstitution after restart
        await using (var db = new TablesDbContext(options))
        {
            var repo = new TableSessionRepository(db);
            var loaded = await repo.GetBy(sessionId1, tableId);
            if (loaded.IsFailure)
                throw new Exception("Failed to load table session state after restart.");

            var state = loaded.Value;
            if (!state.HasPeople || state.PeopleCount != 2 || !state.HasOpenOrders || !state.IsOccupied)
                throw new Exception("Reconstituted table state properties do not match persisted state.");

            if (state.Orders.Count != 1 || state.Orders[0].OrderId != "ord-100")
                throw new Exception("Active order was not reconstituted correctly.");

            var events = await repo.GetEvents(sessionId1, tableId);
            if (events.Count == 0)
                throw new Exception("Events history was not reconstituted.");
        }

        // 5. Concurrency control: prevent silent overwriting
        await using (var dbA = new TablesDbContext(options))
        await using (var dbB = new TablesDbContext(options))
        {
            var repoA = new TableSessionRepository(dbA);
            var repoB = new TableSessionRepository(dbB);

            var stateA = (await repoA.GetBy(sessionId1, tableId)).Value;
            var stateB = (await repoB.GetBy(sessionId1, tableId)).Value;

            var now = DateTimeOffset.UtcNow;
            // Update in context A
            stateA.ApplyVideoObservation(true, 4, false, DataFreshness.Fresh, 20.0, now.AddSeconds(1));
            var saveA = await repoA.Save(stateA);
            if (saveA.IsFailure)
                throw new Exception("Context A save must succeed.");

            // Conflicting update in context B using stale version
            stateB.ApplyVideoObservation(false, 0, false, DataFreshness.Fresh, 21.0, now.AddSeconds(2));
            var saveB = await repoB.Save(stateB);

            if (saveB.IsSuccess)
                throw new Exception("Context B must fail with concurrency conflict and NOT overwrite silently!");

            // Verify context A's update was preserved in database
            await using var verifyDb = new TablesDbContext(options);
            var verifyRepo = new TableSessionRepository(verifyDb);
            var current = (await verifyRepo.GetBy(sessionId1, tableId)).Value;
            if (current.PeopleCount != 4)
                throw new Exception("Context A's update should remain in database without being overwritten.");
        }

        // 6. Idempotent action deduplication
        await using (var db = new TablesDbContext(options))
        {
            var repo = new TableSessionRepository(db);
            var state = (await repo.GetBy(sessionId1, tableId)).Value;
            var now = DateTimeOffset.UtcNow;

            // Make table eligible for cleaning: free it and update order freshness
            state.ApplyVideoObservation(false, 0, false, DataFreshness.Fresh, 50.0, now);
            state.ApplyOrderEvent(new OrderEvent(Guid.NewGuid(), 2, OrderEventType.Closed, "ord-100", tableId, "rest", null,
                OrderStatus.Closed, now, 50.0));
            state.ApplyOrderSnapshot(new OrdersSnapshot("rest", [], now, 2), DataFreshness.Fresh, now);
            await repo.Save(state);

            // Confirm cleaning with actionId
            var actionId = $"act-clean-{Guid.NewGuid():N}";
            state.ConfirmCleaning(55.0, now.AddSeconds(5), actionId);
            var saveFirst = await repo.Save(state);
            if (saveFirst.IsFailure)
                throw new Exception($"Initial cleaning confirmation failed: {saveFirst.Error}");

            var eventsBefore = (await repo.GetEvents(sessionId1, tableId)).Count;

            // Re-execute with same actionId
            state.ConfirmCleaning(55.0, now.AddSeconds(5), actionId);
            var saveSecond = await repo.Save(state);
            if (saveSecond.IsFailure)
                throw new Exception("Repeated idempotent action must succeed.");

            var eventsAfter = (await repo.GetEvents(sessionId1, tableId)).Count;
            if (eventsAfter != eventsBefore)
                throw new Exception("Duplicate event was recorded for an idempotent action!");
        }

        Console.WriteLine("PostgreSQL integration checks passed.");
    }
}
