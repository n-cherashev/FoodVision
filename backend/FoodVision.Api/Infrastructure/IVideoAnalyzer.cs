using FoodVision.Api.Domain;

namespace FoodVision.Api.Infrastructure;

public interface IVideoAnalyzer
{
    Task<IReadOnlyList<TableConfiguration>> ProposeTables(IFormFile video, CancellationToken cancellationToken);
    Task<VideoAnalysis> Analyze(IFormFile video, IReadOnlyList<TableConfiguration> tables, CancellationToken cancellationToken);
}
