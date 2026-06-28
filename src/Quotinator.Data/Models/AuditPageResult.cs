using Quotinator.Data.Entities;

namespace Quotinator.Data.Models;

/// <summary>A paginated slice of the audit log returned by <see cref="Repositories.IAuditReader"/>.</summary>
public sealed record AuditPageResult(
    IReadOnlyList<AuditEntry> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>Total number of pages given <see cref="PageSize"/>.</summary>
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
