namespace Quotinator.Core.Models;

/// <summary>Response shape for <c>GET /api/v1/admin/audit</c> — an <c>Audit_Entry</c> row with every <c>RecordBase</c> column unwrapped to its plain value instead of the internal <c>SafeValue&lt;T&gt;</c> wrapper (#272).</summary>
public sealed class AuditEntryResponse
{
    /// <summary>Canonical (lowercase) id.</summary>
    public required string Id { get; init; }

    /// <summary>Name of the table the operation touched, or <c>"Database"</c> for admin-level actions.</summary>
    public required string TableName { get; init; }

    /// <summary>Guid (lowercase D format) of the affected row, or <see langword="null"/> for bulk or admin-level entries.</summary>
    public string? RecordId { get; init; }

    /// <summary>One of the <see cref="Quotinator.Data.Entities.AuditOperation"/> constants.</summary>
    public required string Operation { get; init; }

    /// <summary>Value from the <c>User-Agent</c> request header, or <see langword="null"/> when no header was present.</summary>
    public string? Agent { get; init; }

    /// <summary>UTC timestamp when the operation was recorded.</summary>
    public DateTime PerformedAt { get; init; }

    /// <summary>UTC timestamp when the record was first written.</summary>
    public DateTime? DateCreated { get; init; }

    /// <summary>UTC timestamp of the most recent update. <see langword="null"/> unless the row was modified outside this project's own normal write path.</summary>
    public DateTime? DateModified { get; init; }

    /// <summary>UTC timestamp when the record was soft-deleted. <see langword="null"/> unless the row was deleted outside this project's own normal write path.</summary>
    public DateTime? DateDeleted { get; init; }

    /// <summary><see langword="true"/> when the record has been soft-deleted.</summary>
    public bool IsDeleted { get; init; }
}
