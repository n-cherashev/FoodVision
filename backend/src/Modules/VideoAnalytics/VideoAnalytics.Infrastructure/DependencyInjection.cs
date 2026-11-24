using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodVision.Api.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddVideoAnalyticsInference(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnalysisOptions>(configuration.GetSection("Analysis"));
        var options = configuration.GetSection("Analysis").Get<AnalysisOptions>() ?? new AnalysisOptions();
        GpuInferenceGate.Configure(options.BenchmarkGpuConcurrency);
        services.AddSingleton<InferenceRuntime>();
        services.AddSingleton<IVideoAnalyzer, VideoAnalyzer>();
        return services;
    }
}
