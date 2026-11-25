using CSharpFunctionalExtensions;
using FoodVision.Tables.Domain;

namespace FoodVision.Tables.Application.Interfaces;

public interface ITableSessionRepository
{
    Task<Result<TableSessionState, string>> GetBy(string sessionId, string tableId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TableSessionState>> GetSessionTables(string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TableEvent>> GetEvents(string sessionId, string tableId, CancellationToken cancellationToken = default);
    Task<Result<TableSessionState, string>> Save(TableSessionState state, CancellationToken cancellationToken = default);
}
