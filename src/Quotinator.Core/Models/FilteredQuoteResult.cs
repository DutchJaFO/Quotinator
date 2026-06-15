namespace Quotinator.Core.Models;

/// <summary>
/// Envelope returned by the random-quote endpoint.
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
}
