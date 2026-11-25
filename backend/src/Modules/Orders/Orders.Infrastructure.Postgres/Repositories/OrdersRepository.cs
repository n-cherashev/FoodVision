using CSharpFunctionalExtensions;
using FoodVision.Orders.Application.Interfaces;
using FoodVision.Orders.Contracts;
using FoodVision.Orders.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FoodVision.Orders.Infrastructure.Postgres.Repositories;

public sealed class OrdersRepository(OrdersDbContext dbContext) : IOrdersRepository
{
    public async Task<Result<Order, string>> GetById(string orderId, CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order == null)
            return Result.Failure<Order, string>($"Заказ '{orderId}' не найден.");

        return Result.Success<Order, string>(order);
    }

    public async Task<IReadOnlyList<Order>> GetByTable(string tableId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Orders
            .Where(o => o.TableId == tableId)
            .OrderByDescending(o => o.OpenedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetBySession(string sessionId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Orders
            .Where(o => o.SessionId == sessionId)
            .OrderBy(o => o.VideoPositionSeconds ?? 0)
            .ThenBy(o => o.OpenedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetActiveOrders(string restaurantId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Orders
            .Where(o => o.RestaurantId == restaurantId &&
                        (o.Status == OrderStatus.Open || o.Status == OrderStatus.Paid))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<Order, string>> Save(Order order, CancellationToken cancellationToken = default)
    {
        var local = dbContext.Orders.Local.FirstOrDefault(o => o.OrderId == order.OrderId);
        if (local == null)
        {
            var exists = await dbContext.Orders.AnyAsync(o => o.OrderId == order.OrderId, cancellationToken);
            if (!exists)
                dbContext.Orders.Add(order);
            else
                dbContext.Orders.Attach(order);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success<Order, string>(order);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<Order, string>("Конфликт параллельного обновления: заказ был изменён другой операцией.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
                                           pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Result.Failure<Order, string>($"Конфликт уникальности: заказ '{order.OrderId}' уже существует.");
        }
    }
}
