using Quotinator.Data.Entities;

namespace Quotinator.Data.Models;

/// <summary>A paginated slice of the import-actions log returned by <see cref="Repositories.ISystemImportActionReader"/>.</summary>
public sealed record SystemImportActionPageResult(
    IReadOnlyList<SystemImportAction> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>Total number of pages given <see cref="PageSize"/>.</summary>
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
