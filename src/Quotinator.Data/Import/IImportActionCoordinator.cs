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
/// This is a new, separate coordinator from <see cref="IConflictResolutionCoordinator"/> (#149),
/// not a generalization of it — #149's shipped, verified code is untouched. Nothing in this
/// coordinator knows what a "Quote" is: <see cref="SystemImportAction.ExistingValue"/>/
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
    /// <exception cref="ImportActionNotFoundException"><paramref name="actionId"/> does not exist.</exception>
    /// <exception cref="ImportActionStateException">The action is already <see cref="ImportActionStatus.Applied"/> or <see cref="ImportActionStatus.Discarded"/>.</exception>
    Task DecideAsync(Guid actionId, string decisionsJson, IDbConnection? connection = null, IDbTransaction? transaction = null);

    /// <summary>
    /// Reverts a staged decision back to <see cref="ImportActionStatus.Pending"/> and clears the
    /// stored decision. Only valid while the action's status is <see cref="ImportActionStatus.Decided"/>.
    /// </summary>
    /// <exception cref="ImportActionNotFoundException"><paramref name="actionId"/> does not exist.</exception>
    /// <exception cref="ImportActionStateException">The action is not currently <see cref="ImportActionStatus.Decided"/>.</exception>
    Task UndoDecisionAsync(Guid actionId, IDbConnection? connection = null, IDbTransaction? transaction = null);

    /// <summary>
    /// Attempts to apply every action sharing <paramref name="batchId"/>. If any are still
    /// <see cref="ImportActionStatus.Pending"/> (no decision recorded), returns their ids and applies
    /// nothing. Otherwise, in one transaction: for every <see cref="ImportActionStatus.Decided"/>
    /// action, invokes <paramref name="applyResolvedAction"/> (the caller-supplied domain-specific
    /// step), then marks the action <see cref="ImportActionStatus.Applied"/>. Commits once, for the
    /// whole batch, only after every action's callback has completed without throwing.
    /// </summary>
    /// <returns><c>null</c> if the whole batch was applied; otherwise the ids still <see cref="ImportActionStatus.Pending"/>.</returns>
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
}
