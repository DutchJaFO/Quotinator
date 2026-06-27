using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>Tracks a discrete group of records introduced into the database together, capturing provenance for all entity rows.</summary>
[Table("ImportBatches")]
public sealed class ImportBatch : RecordBase
{
    /// <summary>Human-readable name identifying the batch (e.g. a filename or dataset name).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Batch category stored as the <see cref="ImportBatchType"/> enum name.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Source URL for <c>Seed</c>-type batches. Null for <c>System</c> and <c>Import</c> batches.</summary>
    public string? Url { get; init; }

    /// <summary>UTC timestamp when the batch was imported, in <c>yyyy-MM-dd HH:mm:ss</c> format.</summary>
    public string ImportedAt { get; init; } = string.Empty;

    /// <summary>UUID of the user who triggered the import. Null for seeded and system batches.</summary>
    public string? ImportedBy { get; init; }

    /// <summary>Number of records written in this batch. Updated after seeding completes.</summary>
    public int RecordCount { get; set; }
}
