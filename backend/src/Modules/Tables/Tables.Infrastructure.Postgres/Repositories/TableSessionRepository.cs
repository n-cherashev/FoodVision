using System.Text.Json;
using CSharpFunctionalExtensions;
using FoodVision.Tables.Application.Interfaces;
using FoodVision.Tables.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FoodVision.Tables.Infrastructure.Postgres.Repositories;

public sealed class TableSessionRepository(TablesDbContext dbContext) : ITableSessionRepository
{
    public async Task<Result<TableSessionState, string>> GetBy(
        string sessionId,
        string tableId,
        CancellationToken cancellationToken = default)
    {
        var state = await dbContext.TableSessionStates
            .FirstOrDefaultAsync(t => t.SessionId == sessionId && t.TableId == tableId, cancellationToken);

        if (state == null)
            return Result.Failure<TableSessionState, string>($"Стол '{tableId}' в сессии '{sessionId}' не найден.");

        LoadHandledActions(state);
        return Result.Success<TableSessionState, string>(state);
    }

    public async Task<IReadOnlyList<TableSessionState>> GetSessionTables(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var list = await dbContext.TableSessionStates
            .Where(t => t.SessionId == sessionId)
            .ToListAsync(cancellationToken);

        foreach (var state in list)
            LoadHandledActions(state);

        return list;
    }

    public async Task<IReadOnlyList<TableEvent>> GetEvents(
        string sessionId,
        string tableId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.TableEvents
            .Where(e => e.SessionId == sessionId && e.TableId == tableId)
            .OrderBy(e => e.VideoPositionSeconds ?? 0)
            .ThenBy(e => e.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<TableSessionState, string>> Save(
        TableSessionState state,
        CancellationToken cancellationToken = default)
    {
        var local = dbContext.TableSessionStates.Local
            .FirstOrDefault(s => s.SessionId == state.SessionId && s.TableId == state.TableId);

        if (local == null)
        {
            var exists = await dbContext.TableSessionStates
                .AnyAsync(s => s.SessionId == state.SessionId && s.TableId == state.TableId, cancellationToken);
            if (!exists)
                dbContext.TableSessionStates.Add(state);
            else
                dbContext.TableSessionStates.Attach(state);
        }

        try
        {
            dbContext.Entry(state).Property<string>("_handledActionsJson").CurrentValue =
                JsonSerializer.Serialize(state.HandledActionIds);
        }
        catch
        {
            // Ignore if entry is not yet tracked or property unavailable
        }

        foreach (var evt in state.UncommittedEvents)
        {
            dbContext.TableEvents.Add(evt);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            state.ClearUncommittedEvents();
            return Result.Success<TableSessionState, string>(state);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<TableSessionState, string>(
                "Конфликт параллельного обновления: состояние стола было изменено другой операцией.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
                                           pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Result.Failure<TableSessionState, string>(
                $"Конфликт дублирующего действия ({pg.ConstraintName}): {pg.MessageText}");
        }
    }

    private void LoadHandledActions(TableSessionState state)
    {
        try
        {
            var json = dbContext.Entry(state).Property<string>("_handledActionsJson").CurrentValue;
            if (!string.IsNullOrWhiteSpace(json))
            {
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null) state.LoadHandledActions(list);
            }
        }
        catch
        {
            // Ignored if not attached or unavailable
        }
    }
}
