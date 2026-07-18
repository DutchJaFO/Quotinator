namespace Quotinator.Data.Models;

/// <summary>A page of results plus the total count across all pages — the shared shape for every paginated read in this codebase, from repository level up through the API response.</summary>
/// <remarks>Deliberately not <c>sealed</c> — a consumer of <c>Quotinator.Data</c> outside this project may need to extend it (e.g. a richer paginated response carrying extra facets for a specific entity) rather than being forced into a parallel type.</remarks>
/// <typeparam name="T">The item type.</typeparam>
public record PagedItems<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    /// <summary>Total number of pages given <see cref="PageSize"/>.</summary>
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
