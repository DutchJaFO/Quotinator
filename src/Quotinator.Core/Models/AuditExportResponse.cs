using Quotinator.Data.Entities;

namespace Quotinator.Core.Models;

/// <summary>
/// Response envelope for <c>GET /api/v1/admin/audit/export</c> (#249) — the full audit trail for a
/// date range in one call, combining both tables the audit-trail concern spans.
/// </summary>
public sealed class AuditExportResponse
{
    /// <summary>Every matching <c>Audit_Entry</c> row, newest first.</summary>
    public required IReadOnlyList<AuditEntryEntity> Entries { get; init; }

    /// <summary>Every matching <c>Audit_Change</c> row, newest first.</summary>
    public required IReadOnlyList<ChangeEntity> Changes { get; init; }
}
