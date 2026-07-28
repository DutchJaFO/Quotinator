namespace Quotinator.Core.Models;

/// <summary>
/// Response envelope for <c>POST /api/v1/import/actions/bulk-decide</c> (#163) — mirrors
/// <see cref="ImportResultResponse"/>'s counts-plus-errors shape for consistency with the rest of the
/// import surface, rather than being invented from scratch.
/// </summary>
public sealed class BulkDecideResponse
{
    /// <summary>Total rows read from the uploaded file, including rows belonging to a failed action group.</summary>
    public required int RowsProcessed { get; init; }

    /// <summary>Number of distinct actions (by <c>ActionId</c>) successfully decided.</summary>
    public required int ActionsDecided { get; init; }

    /// <summary>Action groups that failed and were skipped without aborting the rest of the file.</summary>
    public IReadOnlyList<BulkDecideRowError> Errors { get; init; } = [];
}

/// <summary>One action group that failed during a bulk-decide call, reported instead of aborting the rest of the file.</summary>
public sealed class BulkDecideRowError
{
    /// <summary>The failing group's <c>ActionId</c>, when the file's own value could be parsed.</summary>
    public Guid? ActionId { get; init; }

    /// <summary>Human-readable reason the group was rejected.</summary>
    public required string Message { get; init; }
}
