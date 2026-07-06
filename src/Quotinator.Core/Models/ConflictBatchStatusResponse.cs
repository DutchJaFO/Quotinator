namespace Quotinator.Core.Models;

/// <summary>
/// Returned by <c>POST /api/v1/import/conflicts/apply</c> when a batch could not be applied because
/// one or more of its conflicts still have no recorded decision — git's "unmerged paths" equivalent.
/// </summary>
public sealed class ConflictBatchStatusResponse
{
    /// <summary>The batch that was requested to be applied.</summary>
    public required string BatchId { get; init; }

    /// <summary>Ids of conflicts in this batch that still need a decision before the batch can be applied.</summary>
    public required IReadOnlyList<Guid> PendingConflictIds { get; init; }
}
