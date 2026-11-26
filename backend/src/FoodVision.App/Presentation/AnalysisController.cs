using FoodVision.Api.Features.Analysis;
using FoodVision.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using FoodVision.Shared.Core.Abstractions;

namespace FoodVision.Api.Presentation;

[ApiController]
[Route("api/analysis")]
public sealed class AnalysisController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost("prepare")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Prepare([FromForm] IFormFile video, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send<IReadOnlyList<TableConfiguration>, ProposeTablesRequest>(new(video), cancellationToken);
        if (result.IsSuccess) return Ok(new { tables = result.Value });
        if (result.Error.StartsWith("invalid:", StringComparison.Ordinal)) return BadRequest(new { error = result.Error[8..] });
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error });
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Analyze(
        [FromForm] IFormFile video,
        [FromForm] string layout,
        [FromForm] string? orders,
        [FromForm] string? sessionId,
        CancellationToken cancellationToken)
    {
        TableConfiguration[] tables;
        try { tables = System.Text.Json.JsonSerializer.Deserialize<TableConfiguration[]>(layout, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? []; }
        catch (System.Text.Json.JsonException) { return BadRequest(new { error = "Не удалось прочитать разметку столов." }); }

        TableOrderInterval[] parsedOrders = [];
        if (!string.IsNullOrWhiteSpace(orders))
        {
            try { parsedOrders = System.Text.Json.JsonSerializer.Deserialize<TableOrderInterval[]>(orders, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? []; }
            catch (System.Text.Json.JsonException) { return BadRequest(new { error = "Не удалось прочитать список заказов." }); }
        }

        var result = await dispatcher.Send<VideoAnalysis, AnalyzeVideoRequest>(new(video, tables, parsedOrders), cancellationToken);
        if (result.IsSuccess)
        {
            var analysis = result.Value;
            var effectiveSessionId = string.IsNullOrWhiteSpace(sessionId) ? $"sess-{Guid.NewGuid():N}" : sessionId;

            foreach (var order in parsedOrders)
            {
                var orderId = $"ord-video-{Guid.NewGuid():N}";
                await dispatcher.Send<FoodVision.Orders.Application.DTOs.OrderDetailsDto, FoodVision.Orders.Application.Features.OpenOrderCommand>(
                    new(orderId, order.TableId, "default", order.OrderNumber, effectiveSessionId, null, null, order.GuestsCount, order.StartSeconds, DateTimeOffset.UtcNow), cancellationToken);

                if (order.EndSeconds.HasValue)
                {
                    await dispatcher.Send<FoodVision.Orders.Application.DTOs.OrderDetailsDto, FoodVision.Orders.Application.Features.CloseOrderCommand>(
                        new(orderId, DateTimeOffset.UtcNow, order.EndSeconds.Value), cancellationToken);
                }
            }

            return Ok(new
            {
                analysis.DurationSeconds,
                SessionId = effectiveSessionId,
                Tables = analysis.Tables.Select(table => new
                {
                    table.Id,
                    table.Bounds,
                    Segments = MapVisualSegments(table.Segments)
                })
            });
        }
        if (result.Error.StartsWith("invalid:", StringComparison.Ordinal))
            return BadRequest(new { error = result.Error[8..] });
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error });
    }

    private static IEnumerable<object> MapVisualSegments(IReadOnlyList<FoodVision.Api.Domain.TimeSegment> segments)
    {
        if (segments == null || segments.Count == 0) return [];

        var resolved = new List<FoodVision.Api.Domain.TimeSegment>();
        var lastState = FoodVision.Api.Domain.TableState.Available;

        foreach (var segment in segments)
        {
            var effectiveState = segment.State == FoodVision.Api.Domain.TableState.Review
                ? lastState
                : segment.State;
            lastState = effectiveState;

            var created = FoodVision.Api.Domain.TimeSegment.Create(segment.StartSeconds, segment.EndSeconds, effectiveState);
            if (created.IsSuccess)
            {
                resolved.Add(created.Value);
            }
        }

        // Merge contiguous segments with identical states
        var merged = new List<FoodVision.Api.Domain.TimeSegment>();
        foreach (var segment in resolved)
        {
            if (merged.Count > 0 && merged[^1].State == segment.State)
            {
                var prev = merged[^1];
                var combined = FoodVision.Api.Domain.TimeSegment.Create(prev.StartSeconds, segment.EndSeconds, segment.State);
                if (combined.IsSuccess)
                {
                    merged[^1] = combined.Value;
                }
                else
                {
                    merged.Add(segment);
                }
            }
            else
            {
                merged.Add(segment);
            }
        }

        return merged.Select(segment => new
        {
            segment.StartSeconds,
            segment.EndSeconds,
            State = segment.State switch
            {
                FoodVision.Api.Domain.TableState.OccupiedWithItems => "occupied_with_items",
                _ => segment.State.ToString().ToLowerInvariant()
            }
        });
    }
}
