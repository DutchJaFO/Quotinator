using Quotinator.Data.Entities;

namespace Quotinator.Data.Models;

/// <summary>A paginated slice of the audit log returned by <see cref="Repositories.ISystemAuditReader"/>.</summary>
public sealed record SystemAuditPageResult(
    IReadOnlyList<SystemAuditEntry> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>Total number of pages given <see cref="PageSize"/>.</summary>
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
