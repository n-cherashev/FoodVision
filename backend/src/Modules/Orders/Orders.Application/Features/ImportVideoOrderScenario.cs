using CSharpFunctionalExtensions;
using FoodVision.Orders.Application.DTOs;
using FoodVision.Orders.Application.Interfaces;
using FoodVision.Orders.Contracts;
using FoodVision.Orders.Domain;
using FoodVision.Shared.Core.Abstractions;
using FoodVision.Tables.Application.Features;

namespace FoodVision.Orders.Application.Features;

public sealed record ScenarioItem(
    string ItemId,
    string DishName,
    string Category,
    int Quantity,
    decimal Price,
    int Course = 1,
    double? VideoPositionSeconds = null);

public sealed record ScenarioOrder(
    string OrderId,
    string TableId,
    string? OrderNumber,
    int GuestsCount,
    double OpenVideoSeconds,
    double? ServeDishVideoSeconds,
    double? PayVideoSeconds,
    double? CloseVideoSeconds,
    IReadOnlyList<ScenarioItem>? Items = null);

public sealed record ImportVideoOrderScenarioCommand(
    string SessionId,
    string RestaurantId,
    IReadOnlyList<ScenarioOrder> Orders) : ICommand;

public sealed class ImportVideoOrderScenarioHandler(
    IOrdersRepository repository,
    IDispatcher dispatcher) : ICommandHandler<int, ImportVideoOrderScenarioCommand>
{
    public async Task<Result<int, string>> Handle(
        ImportVideoOrderScenarioCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.SessionId))
            return Result.Failure<int, string>("Идентификатор сессии не может быть пустым.");

        var count = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var spec in command.Orders)
        {
            var openTime = now.AddSeconds(spec.OpenVideoSeconds);
            var orderResult = Order.Create(
                spec.OrderId,
                spec.TableId,
                command.RestaurantId,
                spec.OrderNumber,
                command.SessionId,
                null,
                null,
                spec.GuestsCount,
                spec.OpenVideoSeconds,
                openTime);

            if (orderResult.IsFailure) continue;
            var order = orderResult.Value;

            if (spec.Items != null)
            {
                foreach (var item in spec.Items)
                {
                    order.AddItem(item.ItemId, item.DishName, item.Category, item.Quantity, item.Price, item.Course, item.VideoPositionSeconds);
                }
            }

            if (spec.ServeDishVideoSeconds.HasValue && order.Items.Count > 0)
            {
                order.MarkItemServed(order.Items[0].ItemId, now.AddSeconds(spec.ServeDishVideoSeconds.Value), spec.ServeDishVideoSeconds.Value);
            }

            if (spec.PayVideoSeconds.HasValue)
            {
                order.Pay(order.TotalAmount, now.AddSeconds(spec.PayVideoSeconds.Value), spec.PayVideoSeconds.Value);
            }

            if (spec.CloseVideoSeconds.HasValue)
            {
                order.Close(now.AddSeconds(spec.CloseVideoSeconds.Value), spec.CloseVideoSeconds.Value);
            }

            await repository.Save(order, cancellationToken);
            count++;

            // Propagate active order status to Tables module
            var orderEvt = order.ToEvent(OrderEventType.Opened);
            await dispatcher.Send<FoodVision.Tables.Contracts.TableSessionStateDto, ApplyOrderEventCommand>(
                new(command.SessionId, orderEvt, command.RestaurantId), cancellationToken);

            if (spec.PayVideoSeconds.HasValue)
            {
                var payEvt = order.ToEvent(OrderEventType.Paid, videoPositionSeconds: spec.PayVideoSeconds.Value);
                await dispatcher.Send<FoodVision.Tables.Contracts.TableSessionStateDto, ApplyOrderEventCommand>(
                    new(command.SessionId, payEvt, command.RestaurantId), cancellationToken);
            }

            if (spec.CloseVideoSeconds.HasValue)
            {
                var closeEvt = order.ToEvent(OrderEventType.Closed, videoPositionSeconds: spec.CloseVideoSeconds.Value);
                await dispatcher.Send<FoodVision.Tables.Contracts.TableSessionStateDto, ApplyOrderEventCommand>(
                    new(command.SessionId, closeEvt, command.RestaurantId), cancellationToken);
            }
        }

        return Result.Success<int, string>(count);
    }
}
