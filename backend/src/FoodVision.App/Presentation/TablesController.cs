using FoodVision.Shared.Core.Abstractions;
using FoodVision.Tables.Application.Features;
using FoodVision.Tables.Contracts;
using FoodVision.Tables.Domain;
using Microsoft.AspNetCore.Mvc;

namespace FoodVision.Api.Presentation;

[ApiController]
[Route("api/tables")]
public sealed class TablesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("{sessionId}")]
    public async Task<IActionResult> GetSessionTables(string sessionId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send<IReadOnlyList<TableSessionStateDto>, GetSessionTablesQuery>(
            new(sessionId), cancellationToken);
        return Ok(result.Value);
    }

    [HttpGet("{sessionId}/{tableId}")]
    public async Task<IActionResult> GetTableState(string sessionId, string tableId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send<TableSessionStateDto, GetTableSessionStateQuery>(
            new(sessionId, tableId), cancellationToken);
        if (result.IsFailure) return NotFound(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpGet("{sessionId}/{tableId}/events")]
    public async Task<IActionResult> GetTableEvents(string sessionId, string tableId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send<IReadOnlyList<TableEventDto>, GetTableEventsQuery>(
            new(sessionId, tableId), cancellationToken);
        return Ok(result.Value);
    }

    [HttpPost("cleaning/confirm")]
    public async Task<IActionResult> ConfirmCleaning([FromBody] ConfirmCleaningRequest request, CancellationToken cancellationToken)
    {
        var command = new ConfirmTableCleaningCommand(
            request.SessionId,
            request.TableId,
            request.VideoPositionSeconds,
            request.Timestamp ?? DateTimeOffset.UtcNow);

        var result = await dispatcher.Send<TableSessionStateDto, ConfirmTableCleaningCommand>(command, cancellationToken);
        if (result.IsFailure) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost("observation")]
    public async Task<IActionResult> ApplyObservation([FromBody] ApplyObservationRequest request, CancellationToken cancellationToken)
    {
        var command = new ApplyVideoObservationCommand(
            request.SessionId,
            request.TableId,
            request.IsOccupied,
            request.OccupantsCount,
            request.HasItemsOnTable,
            request.Freshness,
            request.VideoPositionSeconds,
            request.Timestamp ?? DateTimeOffset.UtcNow,
            request.RestaurantId ?? "default");

        var result = await dispatcher.Send<TableSessionStateDto, ApplyVideoObservationCommand>(command, cancellationToken);
        if (result.IsFailure) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpGet("zones")]
    public async Task<IActionResult> GetZones([FromQuery] string restaurantId = "default", CancellationToken cancellationToken = default)
    {
        var result = await dispatcher.Send<IReadOnlyList<TableZoneDto>, GetTableZonesQuery>(new(restaurantId), cancellationToken);
        return Ok(result.Value);
    }

    [HttpPut("zones")]
    public async Task<IActionResult> SaveZones([FromBody] IReadOnlyList<TableZoneDto> zones, [FromQuery] string restaurantId = "default", CancellationToken cancellationToken = default)
    {
        var result = await dispatcher.Send<IReadOnlyList<TableZoneDto>, SaveTableZonesCommand>(new(zones, restaurantId), cancellationToken);
        if (result.IsFailure) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }
}

public sealed record ConfirmCleaningRequest(
    string SessionId,
    string TableId,
    double VideoPositionSeconds = 0,
    DateTimeOffset? Timestamp = null);

public sealed record ApplyObservationRequest(
    string SessionId,
    string TableId,
    bool IsOccupied,
    int OccupantsCount = 0,
    bool HasItemsOnTable = false,
    DataFreshness Freshness = DataFreshness.Fresh,
    double VideoPositionSeconds = 0,
    DateTimeOffset? Timestamp = null,
    string? RestaurantId = null);
