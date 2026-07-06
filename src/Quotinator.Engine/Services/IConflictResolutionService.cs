using Quotinator.Core.Models;
using Quotinator.Engine.Models;

namespace Quotinator.Engine.Services;

/// <summary>
/// Manual conflict-review workflow (#149) — lists pending conflicts and drives the git-merge-style
/// staged decide/undo/apply flow. A thin, Quotinator-specific wrapper over
/// <see cref="Quotinator.Data.Import.IConflictResolutionCoordinator"/>: this class supplies the one
/// domain-specific piece the coordinator needs (how a resolved field map actually gets written to
/// <c>Quotes</c>/<c>Sources</c>/<c>Characters</c>/<c>People</c>) — everything else (staging, undo,
/// batch-readiness checking, the atomic apply transaction) is the coordinator's generic machinery.
/// </summary>
public interface IConflictResolutionService
{
    /// <summary>Returns a paged, filtered list of conflicts with human-readable batch labels attached.</summary>
    Task<ConflictPageResponse> GetPagedAsync(string? batchId, string? status, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a decision for one conflict. Validates immediately — a genuinely ambiguous field left
    /// undecided throws before anything is stored.
    /// </summary>
    Task DecideAsync(Guid conflictId, ConflictDecisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reverts a staged decision back to pending.</summary>
    Task UndoDecisionAsync(Guid conflictId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to apply every conflict sharing <paramref name="batchId"/>. Returns <c>null</c> if the
    /// whole batch was applied; otherwise the ids still pending a decision.
    /// </summary>
    Task<ConflictBatchStatusResponse?> ApplyBatchAsync(string batchId, CancellationToken cancellationToken = default);
}
