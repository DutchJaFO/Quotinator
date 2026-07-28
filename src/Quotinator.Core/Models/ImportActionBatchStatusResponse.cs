namespace Quotinator.Core.Models;

/// <summary>
/// Returned by <c>POST /api/v1/import/actions/apply</c> when a batch could not be applied because
/// one or more of its actions still have no recorded decision.
/// </summary>
public sealed class ImportActionBatchStatusResponse
{
    /// <summary>The batch that was requested to be applied.</summary>
    public required string BatchId { get; init; }

    /// <summary>Ids of actions in this batch that still need a decision before the batch can be applied.</summary>
    public required IReadOnlyList<Guid> PendingActionIds { get; init; }
}
