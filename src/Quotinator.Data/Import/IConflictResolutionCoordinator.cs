using System.Data;
using Quotinator.Data.Entities;

namespace Quotinator.Data.Import;

/// <summary>
/// Generic, domain-agnostic orchestration for a git-merge-style staged conflict-resolution workflow
/// over <see cref="SystemImportConflict"/> (#149). A consumer with their own schema needs to supply
/// only one thing — <c>applyResolvedConflict</c> in <see cref="TryApplyBatchAsync"/>, the single
/// domain-specific step that writes a resolved conflict's values to their own tables. Everything else
/// (staging a decision, undoing one before commit, checking that every conflict in a batch has been
/// decided before anything executes, and atomically applying the whole batch) is fully generic.
/// </summary>
/// <remarks>
/// <see cref="Entities.SystemImportConflict.ExistingValue"/>/<see cref="Entities.SystemImportConflict.IncomingValue"/>/
/// <see cref="Entities.SystemImportConflict.MergedFields"/> are never deserialized here — they stay
/// opaque JSON, exactly as they already are elsewhere in <c>Quotinator.Data</c>. Interpreting them
/// (turning a stored decision back into typed field values) is the domain-specific consumer's job,
/// done inside the callback it supplies to <see cref="TryApplyBatchAsync"/>.
/// </remarks>
public interface IConflictResolutionCoordinator
{
    /// <summary>
    /// Stages a decision for one conflict — transitions <see cref="ImportConflictStatus.Pending"/> to
    /// <see cref="ImportConflictStatus.Decided"/> and stores <paramref name="decisionsJson"/> (already
    /// serialized by the caller — this method never inspects its content) in
    /// <see cref="Entities.SystemImportConflict.MergedFields"/>. Nothing is written to any domain
    /// table. Idempotent — calling again for the same conflict overwrites the prior decision (the
    /// "change your mind before commit" path).
    /// </summary>
    /// <exception cref="ConflictNotFoundException"><paramref name="conflictId"/> does not exist.</exception>
    /// <exception cref="ConflictStateException">The conflict is already <see cref="ImportConflictStatus.Resolved"/>.</exception>
    Task DecideAsync(Guid conflictId, string decisionsJson, IDbConnection? connection = null, IDbTransaction? transaction = null);

    /// <summary>
    /// Reverts a staged decision back to <see cref="ImportConflictStatus.Pending"/> and clears the
    /// stored decision (undo-before-commit). Only valid while the conflict's status is
    /// <see cref="ImportConflictStatus.Decided"/>.
    /// </summary>
    /// <exception cref="ConflictNotFoundException"><paramref name="conflictId"/> does not exist.</exception>
    /// <exception cref="ConflictStateException">The conflict is not currently <see cref="ImportConflictStatus.Decided"/>.</exception>
    Task UndoDecisionAsync(Guid conflictId, IDbConnection? connection = null, IDbTransaction? transaction = null);

    /// <summary>
    /// Attempts to apply every conflict sharing <paramref name="batchId"/>. If any are still
    /// <see cref="ImportConflictStatus.Pending"/> (no decision recorded), returns their ids and applies
    /// nothing — mirrors a git merge refusing to complete with unmerged paths remaining. Otherwise, in
    /// one transaction: for every <see cref="ImportConflictStatus.Decided"/> conflict, invokes
    /// <paramref name="applyResolvedConflict"/> (the caller-supplied domain-specific step — deserialize
    /// the stored decision and field snapshots, resolve the final values, write them to the caller's
    /// own tables), then marks the conflict <see cref="ImportConflictStatus.Resolved"/>. Commits once,
    /// for the whole batch, only after every conflict's callback has completed without throwing.
    /// </summary>
    /// <returns><c>null</c> if the whole batch was applied; otherwise the ids still <see cref="ImportConflictStatus.Pending"/>.</returns>
    Task<IReadOnlyList<Guid>?> TryApplyBatchAsync(
        string batchId,
        Func<SystemImportConflict, IDbConnection, IDbTransaction, Task> applyResolvedConflict,
        CancellationToken cancellationToken = default);
}
