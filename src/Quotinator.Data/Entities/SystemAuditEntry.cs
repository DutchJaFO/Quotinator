using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>Immutable append-only record of a write operation or admin action.</summary>
/// <remarks>
/// Extends <see cref="RecordBase"/> per ADR 002 ("RecordBase applies to all tables without
/// exception") — <see cref="RecordBase.DateModified"/>/<see cref="RecordBase.DateDeleted"/>/
/// <see cref="RecordBase.IsDeleted"/> are never meaningfully used here (an audit entry is never
/// modified or soft-deleted after being written), and <see cref="RecordBase.DateCreated"/>
/// duplicates <see cref="PerformedAt"/> — this redundancy is the ADR's own accepted trade-off
/// in exchange for every table being a full <c>IRepository&lt;T&gt;</c>/<c>IRestorableRepository&lt;T&gt;</c>
/// citizen, not a special case. This was originally built without <see cref="RecordBase"/> (an
/// auto-increment <c>long Id</c> via <c>[Key]</c>) despite the ADR predating that implementation by
/// a week — corrected retroactively via a new migration (<c>AuditMigrations.MigrateToRecordBase</c>)
/// since this table already shipped in v1.7.2, so the column-type change can't be made in place.
/// </remarks>
[Table("System_AuditEntries")]
public sealed class SystemAuditEntry : RecordBase
{
    /// <summary>Name of the table the operation touched, or <c>"Database"</c> for admin-level actions.</summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>
    /// Guid (lowercase D format) of the affected row, or <c>null</c> for bulk or admin-level entries.
    /// A row written under an earlier revision of this project's id-casing convention may still hold
    /// a different casing on disk — <see cref="Quotinator.Data.Queries.Sql.SystemAudit.SelectPaged"/>
    /// reads this column through <c>LOWER(...)</c> so every consumer of this property always sees the
    /// current canonical form regardless of what casing is actually stored (ADR 012's "read-time
    /// presentation normalization" revision).
    /// </summary>
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
