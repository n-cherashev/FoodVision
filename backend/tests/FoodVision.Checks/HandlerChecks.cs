using CSharpFunctionalExtensions;
using FoodVision.Api.Domain;
using FoodVision.Api.Features.Analysis;
using FoodVision.Api.Infrastructure;
using FoodVision.Orders.Application;
using FoodVision.Orders.Application.DTOs;
using FoodVision.Orders.Application.Features;
using FoodVision.Orders.Application.Interfaces;
using FoodVision.Orders.Contracts;
using FoodVision.Orders.Domain;
using FoodVision.Shared.Core.Abstractions;
using FoodVision.Tables.Application.Features;
using FoodVision.Tables.Application.Interfaces;
using FoodVision.Tables.Contracts;
using FoodVision.Tables.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

internal static class HandlerChecks
{
    public static async Task Run()
    {
        var analyzer = new FakeAnalyzer();
        var tableRepo = new FakeTableSessionRepository();
        var ordersRepo = new FakeOrdersRepository();
        var services = new ServiceCollection();
        services.AddSingleton<IVideoAnalyzer>(analyzer);
        services.AddSingleton<ITableSessionRepository>(tableRepo);
        services.AddSingleton<IOrdersRepository>(ordersRepo);
        services.AddHandlers(typeof(AnalyzeVideoHandler).Assembly);
        services.AddHandlers(typeof(ConfirmTableCleaningHandler).Assembly);
        services.AddOrdersApplication();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        using var bytes = new MemoryStream([1]);
        var video = new FormFile(bytes, 0, 1, "video", "test.mp4");
        var proposed = await dispatcher.Send<IReadOnlyList<TableConfiguration>, ProposeTablesRequest>(new(video));
        if (proposed.IsFailure || analyzer.Proposals != 1) throw new Exception("Proposal handler was not dispatched");

        var invalid = await dispatcher.Send<VideoAnalysis, AnalyzeVideoRequest>(new(video, []));
        if (invalid.IsSuccess || !invalid.Error.StartsWith("invalid:", StringComparison.Ordinal) || analyzer.Analyses != 0)
            throw new Exception("Invalid analysis layout must be rejected before invoking the analyzer");

        var layout = new[] { new TableConfiguration("1", new(.2, .2, .2, .2), [new(.1, .1, .5, .5)]) };
        var result = await dispatcher.Send<VideoAnalysis, AnalyzeVideoRequest>(new(video, layout));
        if (result.IsFailure || analyzer.Analyses != 1) throw new Exception("Analysis handler was not dispatched");

        // Verify Table handlers dispatching
        var observationResult = await dispatcher.Send<TableSessionStateDto, ApplyVideoObservationCommand>(
            new("sess-1", "table-1", true, 2, false, DataFreshness.Fresh, 10, DateTimeOffset.UtcNow));
        if (observationResult.IsFailure || !observationResult.Value.IsOccupied)
            throw new Exception("ApplyVideoObservationCommand was not dispatched correctly");

        var cleanFail = await dispatcher.Send<TableSessionStateDto, ConfirmTableCleaningCommand>(
            new("sess-1", "table-1", 10, DateTimeOffset.UtcNow));
        if (cleanFail.IsSuccess)
            throw new Exception("ConfirmTableCleaningCommand should fail when table is occupied");

        // Verify Orders handlers dispatching
        var openOrderResult = await dispatcher.Send<OrderDetailsDto, OpenOrderCommand>(
            new("ord-h1", "table-1", "rest-1", "101", "sess-1", "waiter-1", "Иван", 2, 10.0, DateTimeOffset.UtcNow));
        if (openOrderResult.IsFailure || openOrderResult.Value.Status != OrderStatus.Open)
            throw new Exception($"OpenOrderCommand was not dispatched correctly: {openOrderResult.Error}");

        var addItemResult = await dispatcher.Send<OrderDetailsDto, AddOrderItemCommand>(
            new("ord-h1", "item-1", "Суп", "Первые блюда", 1, 300, 1, 15.0, DateTimeOffset.UtcNow));
        if (addItemResult.IsFailure || addItemResult.Value.TotalAmount != 300)
            throw new Exception($"AddOrderItemCommand was not dispatched correctly: {addItemResult.Error}");

        Console.WriteLine("Handler registration and dispatch checks passed.");
    }

    private sealed class FakeAnalyzer : IVideoAnalyzer
    {
        public int Proposals { get; private set; }
        public int Analyses { get; private set; }
        public Task<IReadOnlyList<TableConfiguration>> ProposeTables(IFormFile video, CancellationToken cancellationToken)
        {
            Proposals++;
            return Task.FromResult<IReadOnlyList<TableConfiguration>>([]);
        }

        public Task<VideoAnalysis> Analyze(IFormFile video, IReadOnlyList<TableConfiguration> tables, IReadOnlyList<TableOrderInterval>? orders, CancellationToken cancellationToken)
        {
            Analyses++;
            return Task.FromResult(VideoAnalysis.Create(1, []).Value);
        }
    }

    private sealed class FakeTableSessionRepository : ITableSessionRepository
    {
        private readonly Dictionary<string, TableSessionState> _store = new();
        private readonly List<TableEvent> _events = new();

        public Task<Result<TableSessionState, string>> GetBy(string sessionId, string tableId, CancellationToken cancellationToken = default)
        {
            var key = $"{sessionId}:{tableId}";
            if (_store.TryGetValue(key, out var state))
                return Task.FromResult(Result.Success<TableSessionState, string>(state));
            return Task.FromResult(Result.Failure<TableSessionState, string>("Not found"));
        }

        public Task<IReadOnlyList<TableSessionState>> GetSessionTables(string sessionId, CancellationToken cancellationToken = default)
        {
            var list = _store.Values.Where(s => s.SessionId == sessionId).ToList();
            return Task.FromResult<IReadOnlyList<TableSessionState>>(list);
        }

        public Task<IReadOnlyList<TableEvent>> GetEvents(string sessionId, string tableId, CancellationToken cancellationToken = default)
        {
            var list = _events.Where(e => e.SessionId == sessionId && e.TableId == tableId).ToList();
            return Task.FromResult<IReadOnlyList<TableEvent>>(list);
        }

        public Task<Result<TableSessionState, string>> Save(TableSessionState state, CancellationToken cancellationToken = default)
        {
            var key = $"{state.SessionId}:{state.TableId}";
            _store[key] = state;
            _events.AddRange(state.UncommittedEvents);
            state.ClearUncommittedEvents();
            return Task.FromResult(Result.Success<TableSessionState, string>(state));
        }
    }

    private sealed class FakeOrdersRepository : IOrdersRepository
    {
        private readonly Dictionary<string, Order> _orders = new();

        public Task<Result<Order, string>> GetById(string orderId, CancellationToken cancellationToken = default)
        {
            if (_orders.TryGetValue(orderId, out var o))
                return Task.FromResult(Result.Success<Order, string>(o));
            return Task.FromResult(Result.Failure<Order, string>("Not found"));
        }

        public Task<IReadOnlyList<Order>> GetByTable(string tableId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Order>>(_orders.Values.Where(o => o.TableId == tableId).ToList());
        }

        public Task<IReadOnlyList<Order>> GetBySession(string sessionId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Order>>(_orders.Values.Where(o => o.SessionId == sessionId).ToList());
        }

        public Task<IReadOnlyList<Order>> GetActiveOrders(string restaurantId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Order>>(_orders.Values.Where(o => o.RestaurantId == restaurantId && o.Status != OrderStatus.Closed && o.Status != OrderStatus.Cancelled).ToList());
        }

        public Task<Result<Order, string>> Save(Order order, CancellationToken cancellationToken = default)
        {
            _orders[order.OrderId] = order;
            return Task.FromResult(Result.Success<Order, string>(order));
        }
    }
}
