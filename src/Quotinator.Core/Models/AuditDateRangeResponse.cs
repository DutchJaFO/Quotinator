namespace Quotinator.Core.Models;

/// <summary>
/// Response envelope for <c>GET /api/v1/admin/audit/date-range</c> (#249) — the earliest/latest
/// timestamp across both <c>Audit_Entry</c> and <c>Audit_Change</c>, so a caller knows what range
/// actually has data before requesting <c>GET /api/v1/admin/audit/export</c>.
/// </summary>
public sealed class AuditDateRangeResponse
{
    /// <summary>The earliest timestamp across both tables, or <c>null</c> when neither has any rows.</summary>
    public required DateTime? EarliestDate { get; init; }

    /// <summary>The latest timestamp across both tables, or <c>null</c> when neither has any rows.</summary>
    public required DateTime? LatestDate { get; init; }
}
