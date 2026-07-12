using System.Data;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Import;

/// <summary>SQLite-backed implementation of <see cref="IImportActionCoordinator"/>.</summary>
public sealed class ImportActionResolutionCoordinator : IImportActionCoordinator
{
    private readonly ISystemImportActionReader _reader;
    private readonly ISystemImportActionWriter _writer;
    private readonly IDbConnectionFactory _factory;

    /// <summary>Initialises the coordinator with the reader/writer it orchestrates and a connection factory for its own transactions.</summary>
    public ImportActionResolutionCoordinator(ISystemImportActionReader reader, ISystemImportActionWriter writer, IDbConnectionFactory factory)
    {
        _reader  = reader;
        _writer  = writer;
        _factory = factory;
    }

    /// <inheritdoc/>
    public async Task StageAsync(IEnumerable<SystemImportAction> actions, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var list = actions as IReadOnlyCollection<SystemImportAction> ?? actions.ToList();
        if (list.Count == 0) return;

        if (connection is not null)
        {
            await _writer.WriteManyAsync(list, connection, transaction);
            return;
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        await _writer.WriteManyAsync(list, conn);
    }

    /// <inheritdoc/>
    public async Task DecideAsync(Guid actionId, string decisionsJson, CompletenessStatus? markCompletenessAs = null, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var action = await _reader.GetByIdAsync(actionId) ?? throw new ImportActionNotFoundException(actionId);
        if (action.Status.Parsed == ImportActionStatus.Applied || action.Status.Parsed == ImportActionStatus.Discarded)
            throw new ImportActionStateException(actionId, action.Status.Raw);

        if (connection is not null)
        {
            await _writer.MarkDecidedAsync(actionId, decisionsJson, markCompletenessAs, connection, transaction);
            return;
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        await _writer.MarkDecidedAsync(actionId, decisionsJson, markCompletenessAs, conn);
    }

    /// <inheritdoc/>
    public async Task UndoDecisionAsync(Guid actionId, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var action = await _reader.GetByIdAsync(actionId) ?? throw new ImportActionNotFoundException(actionId);
        if (action.Status.Parsed != ImportActionStatus.Decided)
            throw new ImportActionStateException(actionId, action.Status.Raw);

        if (connection is not null)
        {
            await _writer.ClearDecisionAsync(actionId, connection, transaction);
            return;
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        await _writer.ClearDecisionAsync(actionId, conn);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>?> TryApplyBatchAsync(
        string batchId,
        Func<SystemImportAction, IDbConnection, IDbTransaction, Task> applyResolvedAction,
        CancellationToken cancellationToken = default)
    {
        var actions = await _reader.GetAllForBatchAsync(batchId);

        // Blocked (#165) holds the whole batch exactly like Pending — a row a human has marked
        // Complete must never be silently modified, and that protection must not be bypassable just
        // because the rest of the batch happens to be ready.
        var pending = actions
            .Where(a => a.Status.Parsed is ImportActionStatus.Pending or ImportActionStatus.Blocked)
            .Select(a => a.Id)
            .ToList();
        if (pending.Count > 0)
            return pending;

        var decided = actions.Where(a => a.Status.Parsed == ImportActionStatus.Decided).ToList();
        if (decided.Count == 0)
            return null; // Nothing left to apply — batch already fully applied/discarded, or had no actions at all.

        using var conn = _factory.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        foreach (var action in decided)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await applyResolvedAction(action, conn, tx);
            await _writer.MarkAppliedAsync(action.Id, conn, tx);
        }

        tx.Commit();
        return null;
    }

    /// <inheritdoc/>
    public async Task DiscardBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        var actions = await _reader.GetAllForBatchAsync(batchId);
        if (actions.Count == 0)
            throw new ImportBatchStateException(batchId, "has no staged actions to discard.");

        if (actions.Any(a => a.Status.Parsed == ImportActionStatus.Applied))
            throw new ImportBatchStateException(batchId, "has already been applied and cannot be discarded.");

        if (actions.All(a => a.Status.Parsed == ImportActionStatus.Discarded))
            throw new ImportBatchStateException(batchId, "has already been discarded.");

        using var conn = _factory.CreateConnection();
        conn.Open();
        await _writer.MarkBatchDiscardedAsync(batchId, conn);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>?> TryReverseBatchAsync(
        string batchId,
        Func<IReadOnlyList<SystemImportAction>, IDbConnection, IDbTransaction, Task> reverseActions,
        CancellationToken cancellationToken = default)
    {
        var actions = await _reader.GetAllForBatchAsync(batchId);

        var notApplied = actions.Where(a => a.Status.Parsed != ImportActionStatus.Applied).Select(a => a.Id).ToList();
        if (notApplied.Count > 0)
            return notApplied;

        if (actions.Count == 0)
            return null; // Nothing to reverse — caller (Engine) is responsible for treating an unknown/empty batch as its own error.

        using var conn = _factory.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        cancellationToken.ThrowIfCancellationRequested();
        await reverseActions(actions, conn, tx);

        tx.Commit();
        return null;
    }
}
