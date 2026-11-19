using FoodVision.Api.Features.Analysis;
using Microsoft.AspNetCore.Mvc;

namespace FoodVision.Api.Presentation;

[ApiController]
[Route("api/analysis")]
public sealed class AnalysisController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Analyze([FromForm] IFormFile video, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Dispatch(new AnalyzeVideoRequest(video), cancellationToken);
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
                        State = segment.State.ToString().ToLowerInvariant()
                    })
                })
            });
        }
        if (result.Error.StartsWith("invalid:", StringComparison.Ordinal))
            return BadRequest(new { error = result.Error[8..] });
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error });
    }
}
