using Quotinator.Data.Entities;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>Read-side operations for the audit log. All queries are append-only reads — the Audit_Entry table is never modified by this interface.</summary>
public interface IAuditEntryReader
{
    /// <summary>Returns a paged list of audit entries, newest first, with an optional table and record-ID filter.</summary>
    Task<PagedItems<AuditEntryEntity>> GetPagedAsync(string? table, string? recordId, int page, int pageSize);
}
