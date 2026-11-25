using FoodVision.Orders.Application.DTOs;
using FoodVision.Orders.Application.Features;
using FoodVision.Shared.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace FoodVision.Api.Presentation;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost("open")]
    public async Task<IActionResult> OpenOrder([FromBody] OpenOrderRequest request, CancellationToken cancellationToken)
    {
        var command = new OpenOrderCommand(
            request.OrderId,
            request.TableId,
            request.RestaurantId ?? "default",
            request.OrderNumber,
            request.SessionId,
            request.WaiterId,
            request.WaiterName,
            request.GuestsCount <= 0 ? 1 : request.GuestsCount,
            request.VideoPositionSeconds,
            request.OpenedAt);

        var result = await dispatcher.Send<OrderDetailsDto, OpenOrderCommand>(command, cancellationToken);
        if (result.IsFailure) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost("{id}/items")]
    public async Task<IActionResult> AddItem(string id, [FromBody] AddOrderItemRequest request, CancellationToken cancellationToken)
    {
        var command = new AddOrderItemCommand(
            id,
            request.ItemId,
            request.DishName,
            request.Category ?? "Основное",
            request.Quantity,
            request.Price,
            request.Course <= 0 ? 1 : request.Course,
            request.VideoPositionSeconds,
            request.Timestamp);

        var result = await dispatcher.Send<OrderDetailsDto, AddOrderItemCommand>(command, cancellationToken);
        if (result.IsFailure) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost("{id}/items/{itemId}/serve")]
    public async Task<IActionResult> ServeItem(string id, string itemId, [FromBody] ServeItemRequest? request, CancellationToken cancellationToken)
    {
        var command = new MarkOrderItemServedCommand(
            id,
            itemId,
            request?.ServedAt ?? DateTimeOffset.UtcNow,
            request?.VideoPositionSeconds);

        var result = await dispatcher.Send<OrderDetailsDto, MarkOrderItemServedCommand>(command, cancellationToken);
        if (result.IsFailure) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost("{id}/pay")]
    public async Task<IActionResult> PayOrder(string id, [FromBody] PayOrderRequest? request, CancellationToken cancellationToken)
    {
        var command = new PayOrderCommand(
            id,
            request?.Amount,
            request?.PaidAt,
            request?.VideoPositionSeconds);

        var result = await dispatcher.Send<OrderDetailsDto, PayOrderCommand>(command, cancellationToken);
        if (result.IsFailure) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost("{id}/close")]
    public async Task<IActionResult> CloseOrder(string id, [FromBody] CloseOrderRequest? request, CancellationToken cancellationToken)
    {
        var command = new CloseOrderCommand(
            id,
            request?.ClosedAt,
            request?.VideoPositionSeconds);

        var result = await dispatcher.Send<OrderDetailsDto, CloseOrderCommand>(command, cancellationToken);
        if (result.IsFailure) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> CancelOrder(string id, [FromBody] CancelOrderRequest request, CancellationToken cancellationToken)
    {
        var command = new CancelOrderCommand(
            id,
            request.Reason ?? "Отменён",
            request.CancelledAt,
            request.VideoPositionSeconds);

        var result = await dispatcher.Send<OrderDetailsDto, CancelOrderCommand>(command, cancellationToken);
        if (result.IsFailure) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost("{id}/transfer")]
    public async Task<IActionResult> TransferOrder(string id, [FromBody] TransferOrderRequest request, CancellationToken cancellationToken)
    {
        var command = new TransferOrderCommand(
            id,
            request.NewTableId,
            request.TransferredAt,
            request.VideoPositionSeconds);

        var result = await dispatcher.Send<OrderDetailsDto, TransferOrderCommand>(command, cancellationToken);
        if (result.IsFailure) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost("video-scenario")]
    public async Task<IActionResult> ImportScenario([FromBody] ImportVideoOrderScenarioCommand command, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send<int, ImportVideoOrderScenarioCommand>(command, cancellationToken);
        if (result.IsFailure) return BadRequest(new { error = result.Error });
        return Ok(new { importedCount = result.Value });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(string id, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send<OrderDetailsDto, GetOrderByIdQuery>(new(id), cancellationToken);
        if (result.IsFailure) return NotFound(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpGet("table/{tableId}")]
    public async Task<IActionResult> GetTableOrders(string tableId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send<IReadOnlyList<OrderDetailsDto>, GetOrdersByTableQuery>(new(tableId), cancellationToken);
        return Ok(result.Value);
    }

    [HttpGet("session/{sessionId}")]
    public async Task<IActionResult> GetSessionOrders(string sessionId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send<IReadOnlyList<OrderDetailsDto>, GetOrdersBySessionQuery>(new(sessionId), cancellationToken);
        return Ok(result.Value);
    }
}

public sealed record OpenOrderRequest(
    string OrderId,
    string TableId,
    string? RestaurantId = null,
    string? OrderNumber = null,
    string? SessionId = null,
    string? WaiterId = null,
    string? WaiterName = null,
    int GuestsCount = 1,
    double? VideoPositionSeconds = null,
    DateTimeOffset? OpenedAt = null);

public sealed record AddOrderItemRequest(
    string ItemId,
    string DishName,
    string? Category = null,
    int Quantity = 1,
    decimal Price = 0,
    int Course = 1,
    double? VideoPositionSeconds = null,
    DateTimeOffset? Timestamp = null);

public sealed record ServeItemRequest(
    DateTimeOffset? ServedAt = null,
    double? VideoPositionSeconds = null);

public sealed record PayOrderRequest(
    decimal? Amount = null,
    DateTimeOffset? PaidAt = null,
    double? VideoPositionSeconds = null);

public sealed record CloseOrderRequest(
    DateTimeOffset? ClosedAt = null,
    double? VideoPositionSeconds = null);

public sealed record CancelOrderRequest(
    string? Reason = null,
    DateTimeOffset? CancelledAt = null,
    double? VideoPositionSeconds = null);

public sealed record TransferOrderRequest(
    string NewTableId,
    DateTimeOffset? TransferredAt = null,
    double? VideoPositionSeconds = null);
