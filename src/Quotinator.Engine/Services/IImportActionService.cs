using Quotinator.Core.Models;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Engine.Models;

namespace Quotinator.Engine.Services;

/// <summary>
/// Unified staging workflow (#154) — decides, undoes, applies, and discards
/// <c>System_ImportAction</c> batches. A thin, Quotinator-specific wrapper over
/// <see cref="Quotinator.Data.Import.IImportActionCoordinator"/>: this class supplies the one
/// domain-specific piece the coordinator needs (how a resolved Quote/Source/Character/Person action
/// actually gets written) — everything else (staging, undo, batch-readiness checking, the atomic
/// apply transaction) is the coordinator's generic machinery.
/// </summary>
public interface IImportActionService
{
    /// <summary>
    /// Returns a paged, filtered list of staged actions — the conflict-review surface for
    /// <c>GET /api/v1/import/actions</c>. Each row's <c>RelatedActionIds</c> links a Quote action to
    /// the Source/Character/Person actions in the same batch it depends on; <c>AmbiguousFields</c> is
    /// populated only for a <c>Pending</c> Quote action.
    /// </summary>
    Task<PagedItems<ImportActionSummaryResponse>> GetPagedAsync(string? batchId, string? status, string? entityType, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a decision for one action of a currently-decidable entity type and <c>ActionType</c>
    /// (today: Quote, and Source Modify — see <see cref="ImportActionNotDecidableException"/>'s own
    /// doc comment for which combination is current, since it changes as more entities gain
    /// decidability). Validates immediately — a genuinely ambiguous field left undecided throws
    /// before anything is stored. Throws <see cref="ImportActionNotDecidableException"/> for any
    /// action whose entity type/<c>ActionType</c> combination isn't currently decidable.
    /// </summary>
    Task DecideAsync(Guid actionId, ConflictDecisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reverts a staged decision back to pending.</summary>
    Task UndoDecisionAsync(Guid actionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to apply every action sharing <paramref name="batchId"/>. Returns <c>null</c> if the
    /// whole batch was applied; otherwise the ids still pending a decision. <paramref name="initiatedByType"/>
    /// records who's applying it (the live import endpoint, startup seeding, or a manual apply of an
    /// already-decided batch) in every <c>System_ChangeLog</c> row this produces.
    /// </summary>
    Task<ImportActionBatchStatusResponse?> ApplyBatchAsync(string batchId, InitiatorType initiatedByType = InitiatorType.WriteEndpoint, CancellationToken cancellationToken = default);

    /// <summary>Discards every action sharing <paramref name="batchId"/>. Never touches any domain table.</summary>
    Task DiscardBatchAsync(string batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses (undoes) every <c>Applied</c> action sharing <paramref name="batchId"/> — #59.
    /// <c>Add</c> actions are soft-deleted; <c>Modify</c> actions are restored to their pre-change
    /// snapshot. Batches undo as a strict global LIFO stack — only the most recently applied batch
    /// still live may be reversed. On success, the batch's own <c>ImportBatch</c> row is itself
    /// soft-deleted, which is the sole signal that its effects are no longer live (its
    /// <c>System_ImportActions</c> rows stay <c>Applied</c> permanently, an accurate historical
    /// record).
    /// </summary>
    /// <param name="batchId">The batch to reverse.</param>
    /// <param name="preview">
    /// When <c>true</c>, runs every blocking check (batch exists, is <c>Applied</c>, is the top of
    /// the stack, has actions to reverse) and returns without writing anything — a caller can tell
    /// whether the real call would succeed without anything changing.
    /// </param>
    /// <param name="initiatedByType">Recorded in every <c>System_ChangeLog</c> row this produces.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ImportBatchNotFoundException">The batch doesn't exist, or is already reversed.</exception>
    /// <exception cref="ImportBatchStateException">The batch isn't currently <c>Applied</c>, isn't the top of the stack, has no actions, or a Modify's original Source/Character/Person linkage can no longer be resolved.</exception>
    Task ReverseBatchAsync(string batchId, bool preview = false, InitiatorType initiatedByType = InitiatorType.WriteEndpoint, CancellationToken cancellationToken = default);
}
