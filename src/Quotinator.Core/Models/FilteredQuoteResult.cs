namespace Quotinator.Core.Models;

/// <summary>
/// Envelope returned by the random-quote and search endpoints.
/// Carries the result set, the size of the matching pool, and a diagnostic status.
/// </summary>
public sealed class FilteredQuoteResult<T>
{
    /// <summary>Outcome of the query.</summary>
    public FilteredResultStatus Status { get; init; }

    /// <summary>Quotes selected from the matching pool. Empty when <see cref="Status"/> is not <see cref="FilteredResultStatus.Ok"/>.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Total number of quotes that matched the filters before the random limit was applied. Zero when <see cref="Items"/> is empty.</summary>
    public int TotalMatching { get; init; }

    /// <summary>Human-readable explanation of a non-Ok status. <c>null</c> when <see cref="Status"/> is <see cref="FilteredResultStatus.Ok"/>.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// Populated only by <c>GET /api/v1/quotes/random</c> — the <c>n</c> the caller requested
    /// (defaulting to 1). <c>null</c> for every other consumer of this envelope (currently
    /// <c>/search</c>).
    /// </summary>
    public int? RequestedCount { get; init; }

    /// <summary>
    /// Populated only by <c>GET /api/v1/quotes/random</c> — <c>Items.Count</c>, surfaced explicitly
    /// so a caller can detect a short result (fewer than <see cref="RequestedCount"/>) without
    /// comparing against its own request. Can be less than <see cref="RequestedCount"/> because the
    /// matching pool was smaller than requested, or because conversation-aware deduplication
    /// excluded quotes that share a conversation with an already-selected one. <c>null</c> for every
    /// other consumer of this envelope.
    /// </summary>
    public int? ReturnedCount { get; init; }
}
