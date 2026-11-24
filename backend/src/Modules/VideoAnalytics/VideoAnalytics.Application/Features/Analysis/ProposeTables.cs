using CSharpFunctionalExtensions;
using FoodVision.Api.Domain;
using FoodVision.Api.Infrastructure;
using FoodVision.Shared.Core.Abstractions;

namespace FoodVision.Api.Features.Analysis;

public sealed record ProposeTablesRequest(IFormFile Video) : ICommand;

public sealed class ProposeTablesHandler(IVideoAnalyzer analyzer) : ICommandHandler<IReadOnlyList<TableConfiguration>, ProposeTablesRequest>
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
