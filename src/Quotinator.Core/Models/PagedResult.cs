namespace Quotinator.Core.Models;

/// <summary>A paginated slice of a result set.</summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>Total number of pages given <see cref="PageSize"/>.</summary>
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
