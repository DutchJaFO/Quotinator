using Dapper.Contrib.Extensions;

namespace Quotinator.Data.Entities;

/// <summary>Immutable append-only record of a write operation or admin action.</summary>
/// <remarks>
/// Does not extend <see cref="Models.RecordBase"/> — audit entries are never soft-deleted
/// or modified. The primary key is an auto-increment long, not a Guid.
/// <c>[Key]</c> on a <c>long</c> property tells Dapper.Contrib to treat it as a server-generated
/// identity column: it is excluded from INSERT and read back after the statement executes.
/// </remarks>
[Table("System_AuditEntries")]
public sealed class SystemAuditEntry
{
    /// <summary>Auto-increment surrogate key assigned by SQLite.</summary>
    [Key]
    public long Id { get; init; }

    /// <summary>Name of the table the operation touched, or <c>"Database"</c> for admin-level actions.</summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>Guid (upper-case D format) of the affected row, or <c>null</c> for bulk or admin-level entries.</summary>
    public string? RecordId { get; init; }

    /// <summary>One of the <see cref="AuditOperation"/> constants.</summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>Value from the <c>User-Agent</c> request header, or <c>null</c> when no header was present.</summary>
    public string? Agent { get; init; }

    /// <summary>UTC timestamp when the operation was recorded.</summary>
    public DateTime PerformedAt { get; init; }
}

/// <summary>String constants for every auditable operation. All values use past tense to match audit-log semantics.</summary>
public static class AuditOperation
{
    // Record-level — written automatically by the repository base class.

    /// <summary>A single record was created.</summary>
    public const string Insert = "Inserted";
    /// <summary>A record was modified.</summary>
    public const string Update = "Updated";
    /// <summary>A record was marked deleted.</summary>
    public const string SoftDelete = "SoftDeleted";
    /// <summary>A soft-deleted record was reinstated.</summary>
    public const string Restore = "Restored";
    /// <summary>A record was permanently removed.</summary>
    public const string HardDelete = "HardDeleted";
    /// <summary>All soft-deleted records in a table were permanently removed.</summary>
    public const string Purge = "Purged";
    /// <summary>A many-to-many join record was created.</summary>
    public const string Link = "Linked";
    /// <summary>A many-to-many join record was removed.</summary>
    public const string Unlink = "Unlinked";
    /// <summary>A batch of records was inserted (one summary entry per batch, not per row).</summary>
    public const string BulkInsert = "BulkInserted";

    // Admin actions — written directly by admin endpoint handlers, not via the repository.

    /// <summary>All data was cleared and reimported from the bundled source files.</summary>
    public const string Reseed = "Reseeded";
    /// <summary>The database schema was dropped and recreated, then reimported from source files.</summary>
    public const string Reset = "Reset";
    /// <summary>A user-provided import file was processed.</summary>
    public const string Import = "Imported";
    /// <summary>A database backup was created.</summary>
    public const string Backup = "BackedUp";
}
