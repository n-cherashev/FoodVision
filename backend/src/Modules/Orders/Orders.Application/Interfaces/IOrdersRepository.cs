using CSharpFunctionalExtensions;
using FoodVision.Orders.Domain;

namespace FoodVision.Orders.Application.Interfaces;

public interface IOrdersRepository
{
    Task<Result<Order, string>> GetById(string orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetByTable(string tableId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetBySession(string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetActiveOrders(string restaurantId, CancellationToken cancellationToken = default);
    Task<Result<Order, string>> Save(Order order, CancellationToken cancellationToken = default);
}
