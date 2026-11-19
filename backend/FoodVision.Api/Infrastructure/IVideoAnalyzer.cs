using FoodVision.Api.Domain;

namespace FoodVision.Api.Infrastructure;

public interface IVideoAnalyzer
{
    Task<VideoAnalysis> Analyze(IFormFile video, CancellationToken cancellationToken);
}
