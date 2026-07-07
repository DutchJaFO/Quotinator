using Dapper.Contrib.Extensions;
using Quotinator.Data.Import;
using Quotinator.Data.Models;

namespace Quotinator.Engine.Entities;

/// <summary>Tracks a discrete group of records introduced into the database together, capturing provenance for all entity rows.</summary>
[Table("ImportBatches")]
public sealed class ImportBatch : RecordBase
{
    /// <summary>Human-readable name identifying the batch (e.g. a filename or dataset name).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Batch category stored as the <see cref="ImportBatchType"/> enum name.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Source URL for externally-sourced <c>Seed</c>-type batches. Null for internally-authored <c>Seed</c> batches, <c>UserSeed</c>, and <c>Import</c> batches.</summary>
    public string? Url { get; init; }

    /// <summary>UTC timestamp when the batch was imported, in <c>yyyy-MM-dd HH:mm:ss</c> format.</summary>
    public string ImportedAt { get; init; } = string.Empty;

    /// <summary>UUID of the user who triggered the import. Null for seeded batches.</summary>
    public string? ImportedBy { get; init; }

    /// <summary>Number of records written in this batch. Updated after seeding completes.</summary>
    public int RecordCount { get; set; }

    /// <summary>The conflict-resolution policy that was active for this batch (the effective policy for quotes, since a batch may span multiple entity types).</summary>
    public SafeValue<DuplicateResolutionPolicy?> ConflictPolicy { get; init; } = SafeValue<DuplicateResolutionPolicy?>.Empty;

    /// <summary>
    /// Batch lifecycle state stored as the <see cref="ImportBatchStatus"/> enum name (#154).
    /// Defaults to <see cref="ImportBatchStatus.Applied"/> — every call site that constructs an
    /// <see cref="ImportBatch"/> without setting this explicitly (every pre-#154 code path: live
    /// import, preview, seeding) always commits immediately, so this default matches their actual
    /// behaviour. Dapper.Contrib's generated INSERT always supplies every property explicitly, so
    /// the column's own SQL-level <c>DEFAULT 'Applied'</c> is never actually reached for a
    /// freshly-constructed entity — this C# default is what really governs new rows.
    /// </summary>
    public string Status { get; set; } = ImportBatchStatus.Applied.ToString();

    /// <summary>UTC timestamp when the batch was applied, in <c>yyyy-MM-dd HH:mm:ss</c> format. Null while <see cref="Status"/> is <see cref="ImportBatchStatus.Staged"/>.</summary>
    public string? AppliedAt { get; set; }
}
