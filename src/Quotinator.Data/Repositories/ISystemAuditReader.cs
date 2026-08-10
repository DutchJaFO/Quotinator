using Quotinator.Data.Entities;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>Read-side operations for the audit log. All queries are append-only reads — the Audit_Entry table is never modified by this interface.</summary>
public interface IAuditEntryReader
{
    /// <summary>Returns a paged list of audit entries, newest first, with an optional table and record-ID filter.</summary>
    Task<PagedItems<AuditEntryEntity>> GetPagedAsync(string? table, string? recordId, int page, int pageSize);

    /// <summary>
    /// Returns every audit entry within an optional date range, newest first, unpaginated (#249's
    /// bulk export endpoint). A <c>null</c> bound is unlimited on that side.
    /// </summary>
    Task<IReadOnlyList<AuditEntryEntity>> GetAllInRangeAsync(DateTime? startDate, DateTime? endDate);

    /// <summary>Matching row count for <see cref="GetAllInRangeAsync"/> — checked against the export row-count cap before assembling the response.</summary>
    Task<int> CountInRangeAsync(DateTime? startDate, DateTime? endDate);

    /// <summary>Earliest/latest <c>PerformedAt</c> across every audit entry, or <c>null</c>/<c>null</c> when the table is empty.</summary>
    Task<(DateTime? Earliest, DateTime? Latest)> GetDateRangeAsync();
}
