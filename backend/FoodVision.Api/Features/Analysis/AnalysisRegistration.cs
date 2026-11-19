using Microsoft.Extensions.DependencyInjection;

namespace FoodVision.Api.Features.Analysis;

public static class AnalysisRegistration
{
    public static IServiceCollection AddAnalysisHandlers(this IServiceCollection services)
    {
        services.AddScoped<IRequestHandler<AnalyzeVideoRequest, FoodVision.Api.Domain.VideoAnalysis>, AnalyzeVideoHandler>();
        return services;
    }
}
