using FoodVision.Api.Features.Analysis;
using FoodVision.Api.Domain;
using Microsoft.AspNetCore.Mvc;

namespace FoodVision.Api.Presentation;

[ApiController]
[Route("api/analysis")]
public sealed class AnalysisController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost("prepare")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Prepare([FromForm] IFormFile video, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Dispatch(new ProposeTablesRequest(video), cancellationToken);
        if (result.IsSuccess) return Ok(new { tables = result.Value });
        if (result.Error.StartsWith("invalid:", StringComparison.Ordinal)) return BadRequest(new { error = result.Error[8..] });
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error });
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Analyze([FromForm] IFormFile video, [FromForm] string layout, CancellationToken cancellationToken)
    {
        TableConfiguration[] tables;
        try { tables = System.Text.Json.JsonSerializer.Deserialize<TableConfiguration[]>(layout, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? []; }
        catch (System.Text.Json.JsonException) { return BadRequest(new { error = "Не удалось прочитать разметку столов." }); }
        var result = await dispatcher.Dispatch(new AnalyzeVideoRequest(video, tables), cancellationToken);
        if (result.IsSuccess)
        {
            var analysis = result.Value;
            return Ok(new
            {
                analysis.DurationSeconds,
                Tables = analysis.Tables.Select(table => new
                {
                    table.Id,
                    table.Bounds,
                    Segments = table.Segments.Select(segment => new
                    {
                        segment.StartSeconds, segment.EndSeconds,
                        State = segment.State switch
                        {
                            FoodVision.Api.Domain.TableState.OccupiedWithItems => "occupied_with_items",
                            _ => segment.State.ToString().ToLowerInvariant()
                        }
                    })
                })
            });
        }
        if (result.Error.StartsWith("invalid:", StringComparison.Ordinal))
            return BadRequest(new { error = result.Error[8..] });
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error });
    }
}
