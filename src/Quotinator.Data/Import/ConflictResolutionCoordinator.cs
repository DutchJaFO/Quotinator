using System.Data;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Import;

/// <summary>SQLite-backed implementation of <see cref="IConflictResolutionCoordinator"/>.</summary>
public sealed class ConflictResolutionCoordinator : IConflictResolutionCoordinator
{
    private readonly ISystemImportConflictReader _reader;
    private readonly ISystemImportConflictWriter _writer;
    private readonly IDbConnectionFactory _factory;

    /// <summary>Initialises the coordinator with the reader/writer it orchestrates and a connection factory for its own transactions.</summary>
    public ConflictResolutionCoordinator(ISystemImportConflictReader reader, ISystemImportConflictWriter writer, IDbConnectionFactory factory)
    {
        _reader  = reader;
        _writer  = writer;
        _factory = factory;
    }

    /// <inheritdoc/>
    public async Task DecideAsync(Guid conflictId, string decisionsJson, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var conflict = await _reader.GetByIdAsync(conflictId) ?? throw new ConflictNotFoundException(conflictId);
        if (conflict.Status == ImportConflictStatus.Resolved)
            throw new ConflictStateException(conflictId, conflict.Status);

        if (connection is not null)
        {
            await _writer.MarkDecidedAsync(conflictId, decisionsJson, connection, transaction);
            return;
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        await _writer.MarkDecidedAsync(conflictId, decisionsJson, conn);
    }

    /// <inheritdoc/>
    public async Task UndoDecisionAsync(Guid conflictId, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var conflict = await _reader.GetByIdAsync(conflictId) ?? throw new ConflictNotFoundException(conflictId);
        if (conflict.Status != ImportConflictStatus.Decided)
            throw new ConflictStateException(conflictId, conflict.Status);

        if (connection is not null)
        {
            await _writer.ClearDecisionAsync(conflictId, connection, transaction);
            return;
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        await _writer.ClearDecisionAsync(conflictId, conn);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>?> TryApplyBatchAsync(
        string batchId,
        Func<SystemImportConflict, IDbConnection, IDbTransaction, Task> applyResolvedConflict,
        CancellationToken cancellationToken = default)
    {
        var conflicts = await _reader.GetAllForBatchAsync(batchId);

        var pending = conflicts.Where(c => c.Status == ImportConflictStatus.Pending).Select(c => c.Id).ToList();
        if (pending.Count > 0)
            return pending;

        var decided = conflicts.Where(c => c.Status == ImportConflictStatus.Decided).ToList();
        if (decided.Count == 0)
            return null; // Nothing left to apply — batch already fully resolved (or had no conflicts at all).

        using var conn = _factory.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        foreach (var conflict in decided)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await applyResolvedConflict(conflict, conn, tx);
            await _writer.MarkResolvedAsync(conflict.Id, conn, tx);
        }

        tx.Commit();
        return null;
    }
}
