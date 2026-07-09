using Quotinator.Core.Models;
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
    Task<ImportActionPageResponse> GetPagedAsync(string? batchId, string? status, string? entityType, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a decision for one Quote action. Validates immediately — a genuinely ambiguous field
    /// left undecided throws before anything is stored. Throws <see cref="ImportActionNotDecidableException"/>
    /// for a Source/Character/Person action (always already-Decided; never a valid decide target).
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
}
