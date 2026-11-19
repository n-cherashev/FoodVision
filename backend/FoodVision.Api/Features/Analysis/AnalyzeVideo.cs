using CSharpFunctionalExtensions;
using FoodVision.Api.Domain;
using FoodVision.Api.Infrastructure;

namespace FoodVision.Api.Features.Analysis;

public sealed record AnalyzeVideoRequest(IFormFile Video) : IRequest<VideoAnalysis>;

public sealed class AnalyzeVideoHandler(IVideoAnalyzer analyzer) : IRequestHandler<AnalyzeVideoRequest, VideoAnalysis>
{
    public async Task<Result<VideoAnalysis, string>> Handle(AnalyzeVideoRequest request, CancellationToken cancellationToken)
    {
        if (request.Video.Length == 0) return Result.Failure<VideoAnalysis, string>("invalid:Выбранный файл пуст.");
        var extension = Path.GetExtension(request.Video.FileName);
        if (!new[] { ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".webm" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<VideoAnalysis, string>("invalid:Поддерживаются видео MP4, MOV, M4V, AVI, MKV и WebM.");

        try { return Result.Success<VideoAnalysis, string>(await analyzer.Analyze(request.Video, cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return Result.Failure<VideoAnalysis, string>(exception.Message); }
    }
}
