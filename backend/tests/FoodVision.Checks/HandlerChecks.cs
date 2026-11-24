using FoodVision.Api.Domain;
using FoodVision.Api.Features.Analysis;
using FoodVision.Api.Infrastructure;
using FoodVision.Shared.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

internal static class HandlerChecks
{
    public static async Task Run()
    {
        var analyzer = new FakeAnalyzer();
        var services = new ServiceCollection();
        services.AddSingleton<IVideoAnalyzer>(analyzer);
        services.AddHandlers(typeof(AnalyzeVideoHandler).Assembly);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        using var bytes = new MemoryStream([1]);
        var video = new FormFile(bytes, 0, 1, "video", "test.mp4");
        var proposed = await dispatcher.Send<IReadOnlyList<TableConfiguration>, ProposeTablesRequest>(new(video));
        if (proposed.IsFailure || analyzer.Proposals != 1) throw new Exception("Proposal handler was not dispatched");

        var invalid = await dispatcher.Send<VideoAnalysis, AnalyzeVideoRequest>(new(video, []));
        if (invalid.IsSuccess || !invalid.Error.StartsWith("invalid:", StringComparison.Ordinal) || analyzer.Analyses != 0)
            throw new Exception("Invalid analysis layout must be rejected before invoking the analyzer");

        var layout = new[] { new TableConfiguration("1", new(.2, .2, .2, .2), [new(.1, .1, .5, .5)]) };
        var result = await dispatcher.Send<VideoAnalysis, AnalyzeVideoRequest>(new(video, layout));
        if (result.IsFailure || analyzer.Analyses != 1) throw new Exception("Analysis handler was not dispatched");
        Console.WriteLine("Handler registration and dispatch checks passed.");
    }

    private sealed class FakeAnalyzer : IVideoAnalyzer
    {
        public int Proposals { get; private set; }
        public int Analyses { get; private set; }
        public Task<IReadOnlyList<TableConfiguration>> ProposeTables(IFormFile video, CancellationToken cancellationToken)
        {
            Proposals++;
            return Task.FromResult<IReadOnlyList<TableConfiguration>>([]);
        }

        public Task<VideoAnalysis> Analyze(IFormFile video, IReadOnlyList<TableConfiguration> tables, CancellationToken cancellationToken)
        {
            Analyses++;
            return Task.FromResult(VideoAnalysis.Create(1, []).Value);
        }
    }
}
