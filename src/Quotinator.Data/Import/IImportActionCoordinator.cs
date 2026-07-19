using System.Data;
using Quotinator.Data.Entities;

namespace Quotinator.Data.Import;

/// <summary>
/// Generic, domain-agnostic orchestration for a stage → decide/undo → apply/discard workflow over
/// <see cref="SystemImportAction"/> (#154). A consumer with their own schema needs to supply only
/// two things — the already-classified actions passed to <see cref="StageAsync"/> (produced by the
/// consumer's own classifier) and <c>applyResolvedAction</c> in <see cref="TryApplyBatchAsync"/>
/// (the domain-specific step that writes a decided action's values to the consumer's own tables).
/// Everything else — staging, deciding, undoing a decision, checking that every action in a batch
/// has been decided before anything executes, atomically applying the whole batch, and discarding a
/// batch outright — is fully generic.
/// </summary>
/// <remarks>
/// Nothing in this coordinator knows what a "Quote" is: <see cref="SystemImportAction.ExistingValue"/>/
/// <see cref="SystemImportAction.IncomingValue"/>/<see cref="SystemImportAction.MergedFields"/> are
/// never deserialized here; they stay opaque JSON.
/// </remarks>
public interface IImportActionCoordinator
{
    /// <summary>
    /// Persists every already-classified action for a batch in one call. The caller (a
    /// domain-specific classifier) decides what each action's <see cref="SystemImportAction.Status"/>
    /// starts as — <see cref="ImportActionStatus.Pending"/> for a genuinely ambiguous Modify,
    /// <see cref="ImportActionStatus.Decided"/> for everything else (an Add, or an unambiguous
    /// Modify) — this method just writes what it's given.
    /// </summary>
    Task StageAsync(IEnumerable<SystemImportAction> actions, IDbConnection? connection = null, IDbTransaction? transaction = null);

    /// <summary>
    /// Stages a decision for one action — transitions <see cref="ImportActionStatus.Pending"/> to
    /// <see cref="ImportActionStatus.Decided"/> and stores <paramref name="decisionsJson"/> (already
    /// serialized by the caller — this method never inspects its content) in
    /// <see cref="SystemImportAction.MergedFields"/>. Nothing is written to any domain table.
    /// Idempotent — calling again for the same action overwrites the prior decision.
    /// </summary>
    /// <paramref name="markCompletenessAs"/> (#165) optionally sets the target record's completeness
    /// status directly when this decision is later applied — recorded now, consumed by the caller's
    /// own <c>applyResolvedAction</c> callback at apply time. Always overwrites any previously-set
    /// value, including with <c>null</c>, on a repeated decide call for the same action.
    /// <exception cref="ImportActionNotFoundException"><paramref name="actionId"/> does not exist.</exception>
    /// <exception cref="ImportActionStateException">The action is already <see cref="ImportActionStatus.Applied"/> or <see cref="ImportActionStatus.Discarded"/>.</exception>
    Task DecideAsync(Guid actionId, string decisionsJson, CompletenessStatus? markCompletenessAs = null, IDbConnection? connection = null, IDbTransaction? transaction = null);

    /// <summary>
    /// Reverts a staged decision back to <see cref="ImportActionStatus.Pending"/> and clears the
    /// stored decision. Only valid while the action's status is <see cref="ImportActionStatus.Decided"/>.
    /// </summary>
    /// <exception cref="ImportActionNotFoundException"><paramref name="actionId"/> does not exist.</exception>
    /// <exception cref="ImportActionStateException">The action is not currently <see cref="ImportActionStatus.Decided"/>.</exception>
    Task UndoDecisionAsync(Guid actionId, IDbConnection? connection = null, IDbTransaction? transaction = null);

    /// <summary>
    /// Attempts to apply every action sharing <paramref name="batchId"/>. If any are still
    /// <see cref="ImportActionStatus.Pending"/> or <see cref="ImportActionStatus.Blocked"/> (#165 —
    /// no decision recorded, or held for completeness review), returns their ids and applies nothing
    /// — a <c>Blocked</c> action holds the entire batch, not just itself. Otherwise, in one
    /// transaction: for every <see cref="ImportActionStatus.Decided"/> action, invokes
    /// <paramref name="applyResolvedAction"/> (the caller-supplied domain-specific step), then marks
    /// the action <see cref="ImportActionStatus.Applied"/>. Commits once, for the whole batch, only
    /// after every action's callback has completed without throwing.
    /// </summary>
    /// <returns><c>null</c> if the whole batch was applied; otherwise the ids still <see cref="ImportActionStatus.Pending"/> or <see cref="ImportActionStatus.Blocked"/>.</returns>
    Task<IReadOnlyList<Guid>?> TryApplyBatchAsync(
        string batchId,
        Func<SystemImportAction, IDbConnection, IDbTransaction, Task> applyResolvedAction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks every action sharing <paramref name="batchId"/> as <see cref="ImportActionStatus.Discarded"/>
    /// in one statement. Never touches any domain table — a discarded batch's Add actions never
    /// created anything to begin with (creation is deferred to apply time).
    /// </summary>
    /// <exception cref="ImportBatchStateException">The batch has no staged actions, or any action sharing it is already <see cref="ImportActionStatus.Applied"/> or <see cref="ImportActionStatus.Discarded"/>.</exception>
    Task DiscardBatchAsync(string batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to reverse every action sharing <paramref name="batchId"/> — #59's batch-undo. If
    /// any action isn't <see cref="ImportActionStatus.Applied"/> yet (still <see cref="ImportActionStatus.Pending"/>/
    /// <see cref="ImportActionStatus.Decided"/>, or already <see cref="ImportActionStatus.Discarded"/>),
    /// returns their ids and reverses nothing — the symmetric mirror of <see cref="TryApplyBatchAsync"/>'s
    /// "refuse if anything is still Pending" contract, checking the opposite condition. Otherwise, in
    /// one transaction, invokes <paramref name="reverseActions"/> once with the batch's full action
    /// list (unlike <see cref="TryApplyBatchAsync"/>'s per-action callback — reversal's own entity
    /// ordering requirement needs the whole batch visible at once, which a per-action callback can't
    /// provide without this coordinator knowing entity-type semantics ADR 004 says it shouldn't).
    /// Commits once the callback completes without throwing.
    /// <para/>
    /// Unlike <see cref="ImportActionStatus.Applied"/>, this coordinator never introduces a
    /// <c>Reversed</c> action status — every action stays <see cref="ImportActionStatus.Applied"/>
    /// permanently, an accurate historical record of what was done. Whether a batch's effects are
    /// still live is entirely the consuming project's own concern (e.g. Quotinator.Core soft-deletes
    /// its own <c>ImportBatch</c> row inside <paramref name="reverseActions"/> itself) — this
    /// coordinator has no domain-level batch concept to update.
    /// </summary>
    /// <returns><c>null</c> if the whole batch was reversed; otherwise the ids not yet <see cref="ImportActionStatus.Applied"/>.</returns>
    Task<IReadOnlyList<Guid>?> TryReverseBatchAsync(
        string batchId,
        Func<IReadOnlyList<SystemImportAction>, IDbConnection, IDbTransaction, Task> reverseActions,
        CancellationToken cancellationToken = default);
}
