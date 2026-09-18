using Quotinator.Data.Enums;
using System.Data;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Import;

/// <summary>SQLite-backed implementation of <see cref="IImportActionCoordinator"/>.</summary>
/// <remarks>Initialises the coordinator with the reader/writer it orchestrates and a connection factory for its own transactions.</remarks>
/// <param name="reader">Reader used to look up staged import actions and their current state.</param>
/// <param name="writer">Writer used to stage, decide, and apply/discard import actions.</param>
/// <param name="factory">Factory used to open connections and transactions for operations with no caller-supplied connection.</param>
public sealed class ImportActionResolutionCoordinator(IImportActionReader reader, IImportActionWriter writer, IDbConnectionFactory factory) : IImportActionCoordinator
{
    private readonly IImportActionReader _reader = reader;
    private readonly IImportActionWriter _writer = writer;
    private readonly IDbConnectionFactory _factory = factory;

    /// <inheritdoc/>
    public async Task StageAsync(IEnumerable<ImportActionEntity> actions, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        IReadOnlyCollection<ImportActionEntity> list = actions as IReadOnlyCollection<ImportActionEntity> ?? [.. actions];
        if (list.Count == 0) return;

        if (connection is not null)
        {
            await _writer.WriteManyAsync(list, connection, transaction);
            return;
        }

        using IDbConnection conn = _factory.CreateConnection();
        conn.Open();
        await _writer.WriteManyAsync(list, conn);
    }

    /// <inheritdoc/>
    public async Task<ImportActionDecideResult> DecideAsync(Guid actionId, string decisionsJson, CompletenessStatus? markCompletenessAs = null, string? originalDecisionJson = null, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        ImportActionEntity action = await _reader.GetByIdAsync(actionId) ?? throw new ImportActionNotFoundException(actionId);
        if (action.Status.Parsed == ImportActionStatus.Applied || action.Status.Parsed == ImportActionStatus.Discarded)
            throw new ImportActionStateException(actionId, action.Status.Raw);

        if (connection is not null)
        {
            await _writer.MarkDecidedAsync(actionId, decisionsJson, markCompletenessAs, originalDecisionJson, connection, transaction);
            return ImportActionDecideResult.Decided(actionId);
        }

        using IDbConnection conn = _factory.CreateConnection();
        conn.Open();
        await _writer.MarkDecidedAsync(actionId, decisionsJson, markCompletenessAs, originalDecisionJson, conn);
        return ImportActionDecideResult.Decided(actionId);
    }

    /// <inheritdoc/>
    public async Task UndoDecisionAsync(Guid actionId, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        ImportActionEntity action = await _reader.GetByIdAsync(actionId) ?? throw new ImportActionNotFoundException(actionId);
        if (action.Status.Parsed != ImportActionStatus.Decided)
            throw new ImportActionStateException(actionId, action.Status.Raw);

        if (connection is not null)
        {
            await _writer.ClearDecisionAsync(actionId, connection, transaction);
            return;
        }

        using IDbConnection conn = _factory.CreateConnection();
        conn.Open();
        await _writer.ClearDecisionAsync(actionId, conn);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>?> TryApplyBatchAsync(
        string batchId,
        Func<ImportActionEntity, IDbConnection, IDbTransaction, Task> applyResolvedAction,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ImportActionEntity> actions = await _reader.GetAllForBatchAsync(batchId);

        // Blocked and Pending (#165) hold the whole batch exactly like Stale — a row a human has marked
        // Complete must never be silently modified, and that protection must not be bypassable just
        // because the rest of the batch happens to be ready. Stale (#153) holds it the same way — a
        // rule whose recorded snapshot no longer matches reality must never be silently reapplied.
        //
        // #374: a Blocked or Pending Add is the one exception, decided narrowly rather than added as a
        // new status. Every other Blocked/Pending action in this codebase pairs with Modify — it
        // protects an *existing* row from being silently changed, which is a property of that row and
        // reasonably holds the whole batch until a human looks at it. A Blocked/Pending Add protects
        // nothing that already exists; it means this one brand-new thing needs its own review (e.g. two
        // quotes with identical text under one Source, or a series-capable Source's own date conflict)
        // — unrelated Adds/Modifies elsewhere in the same batch have no logical dependency on it and
        // must not wait on it. Found live: without this extended to Pending too, three tv-date-conflict
        // quotes held an entire 1,200+-quote reseed to nothing applied at all.
        //
        // #374 (second exception): a Blocked or Pending *Modify* whose ExistingBatchId equals its own
        // BatchId protects nothing that already exists either — its "existing" side is itself just an
        // earlier row from this same batch (e.g. two lines in one import file that hash to the same id
        // and disagree only by case), not a genuinely stored, previously-committed row. A real
        // DB-backed Modify's ExistingBatchId always names whichever earlier batch actually wrote that
        // row, so this can never be mistaken for the protection the general rule above exists to give.
        // Found live: a case-only same-file duplicate newly staged as Pending (per the case-sensitive
        // content-field rule) held an entire cold-start seed to zero rows written.
        List<Guid> pending = [.. actions
            .Where(a => a.Status.Parsed is ImportActionStatus.Stale
                || (a.Status.Parsed is ImportActionStatus.Blocked or ImportActionStatus.Pending
                    && a.ActionType.Parsed is not ImportActionKind.Add
                    && a.ExistingBatchId != a.BatchId))
            .Select(a => a.Id)];
        if (pending.Count > 0)
            return pending;

        List<ImportActionEntity> decided = [.. actions.Where(a => a.Status.Parsed == ImportActionStatus.Decided)];
        if (decided.Count == 0)
            return null; // Nothing left to apply — batch already fully applied/discarded, or had no actions at all.

        using IDbConnection conn = _factory.CreateConnection();
        conn.Open();
        using IDbTransaction tx = conn.BeginTransaction();

        foreach (ImportActionEntity action in decided)
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
        IReadOnlyList<ImportActionEntity> actions = await _reader.GetAllForBatchAsync(batchId);
        if (actions.Count == 0)
            throw new ImportBatchStateException(batchId, "has no staged actions to discard.");

        // A kind that cannot be read counts as real work: refusing is the side a wrong guess recovers from.
        if (actions.Any(a => a.Status.Parsed == ImportActionStatus.Applied && a.ActionType.Parsed?.IsPlanTimeNoOp() != true))
            throw new ImportBatchStateException(batchId, "has already been applied and cannot be discarded.");

        if (actions.All(a => a.Status.Parsed == ImportActionStatus.Discarded))
            throw new ImportBatchStateException(batchId, "has already been discarded.");

        if (actions.All(a => a.Status.Parsed is ImportActionStatus.Applied or ImportActionStatus.Discarded))
            throw new ImportBatchStateException(batchId, "has nothing awaiting a decision to discard.");

        using IDbConnection conn = _factory.CreateConnection();
        conn.Open();
        await _writer.MarkBatchDiscardedAsync(batchId, conn);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>?> TryReverseBatchAsync(
        string batchId,
        Func<IReadOnlyList<ImportActionEntity>, IDbConnection, IDbTransaction, Task> reverseActions,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ImportActionEntity> actions = await _reader.GetAllForBatchAsync(batchId);

        List<Guid> notApplied = [.. actions.Where(a => a.Status.Parsed != ImportActionStatus.Applied).Select(a => a.Id)];
        if (notApplied.Count > 0)
            return notApplied;

        if (actions.Count == 0)
            return null; // Nothing to reverse — caller (Engine) is responsible for treating an unknown/empty batch as its own error.

        using IDbConnection conn = _factory.CreateConnection();
        conn.Open();
        using IDbTransaction tx = conn.BeginTransaction();

        cancellationToken.ThrowIfCancellationRequested();
        await reverseActions(actions, conn, tx);

        tx.Commit();
        return null;
    }
}
