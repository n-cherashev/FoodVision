using CSharpFunctionalExtensions;
using FoodVision.Api.Domain;
using FoodVision.Api.Infrastructure;

namespace FoodVision.Api.Features.Analysis;

public sealed record AnalyzeVideoRequest(IFormFile Video, IReadOnlyList<TableConfiguration> Tables) : IRequest<VideoAnalysis>;
public sealed record ProposeTablesRequest(IFormFile Video) : IRequest<IReadOnlyList<TableConfiguration>>;

public sealed class AnalyzeVideoHandler(IVideoAnalyzer analyzer) : IRequestHandler<AnalyzeVideoRequest, VideoAnalysis>
{
    public async Task<Result<VideoAnalysis, string>> Handle(AnalyzeVideoRequest request, CancellationToken cancellationToken)
    {
        if (request.Video.Length == 0) return Result.Failure<VideoAnalysis, string>("invalid:Выбранный файл пуст.");
        var extension = Path.GetExtension(request.Video.FileName);
        if (!new[] { ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".webm" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<VideoAnalysis, string>("invalid:Поддерживаются видео MP4, MOV, M4V, AVI, MKV и WebM.");

        if (request.Tables.Any(table => table is null || table.Bounds is null || table.SeatingZones is null))
            return Result.Failure<VideoAnalysis, string>("invalid:Разметка должна содержать границы столов и зоны посадки.");
        if (request.Tables.Count == 0) return Result.Failure<VideoAnalysis, string>("invalid:Добавьте хотя бы один стол перед анализом.");
        if (request.Tables.Any(table => string.IsNullOrWhiteSpace(table.Id)) || request.Tables.Select(table => table.Id).Distinct(StringComparer.Ordinal).Count() != request.Tables.Count)
            return Result.Failure<VideoAnalysis, string>("invalid:У каждого стола должен быть уникальный идентификатор.");
        if (request.Tables.Any(table => !ValidBounds(table.Bounds) || table.SeatingZones.Count == 0 || table.SeatingZones.Any(zone => !ValidBounds(zone))))
            return Result.Failure<VideoAnalysis, string>("invalid:Проверьте границы столов и добавьте зону посадки для каждого стола.");

        try { return Result.Success<VideoAnalysis, string>(await analyzer.Analyze(request.Video, request.Tables, cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return Result.Failure<VideoAnalysis, string>(exception.Message); }
    }

    private static bool ValidBounds(NormalizedBounds? bounds) => bounds is not null &&
        double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) &&
        bounds.X >= 0 && bounds.Y >= 0 && bounds.Width > 0 && bounds.Height > 0 && bounds.X + bounds.Width <= 1 && bounds.Y + bounds.Height <= 1;
}

public sealed class ProposeTablesHandler(IVideoAnalyzer analyzer) : IRequestHandler<ProposeTablesRequest, IReadOnlyList<TableConfiguration>>
{
    public async Task<CSharpFunctionalExtensions.Result<IReadOnlyList<TableConfiguration>, string>> Handle(ProposeTablesRequest request, CancellationToken cancellationToken)
    {
        if (request.Video.Length == 0) return CSharpFunctionalExtensions.Result.Failure<IReadOnlyList<TableConfiguration>, string>("invalid:Выбранный файл пуст.");
        var extension = Path.GetExtension(request.Video.FileName);
        if (!new[] { ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".webm" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return CSharpFunctionalExtensions.Result.Failure<IReadOnlyList<TableConfiguration>, string>("invalid:Поддерживаются видео MP4, MOV, M4V, AVI, MKV и WebM.");
        try { return CSharpFunctionalExtensions.Result.Success<IReadOnlyList<TableConfiguration>, string>(await analyzer.ProposeTables(request.Video, cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return CSharpFunctionalExtensions.Result.Failure<IReadOnlyList<TableConfiguration>, string>(exception.Message); }
    }
}
