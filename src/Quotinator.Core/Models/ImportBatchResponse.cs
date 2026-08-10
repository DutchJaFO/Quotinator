namespace Quotinator.Core.Models;

/// <summary>Response shape for <c>GET /api/v1/import/batches</c> (list) and <c>GET /api/v1/import/batches/{id}</c> (detail) — #251.</summary>
public sealed class ImportBatchResponse
{
    /// <summary>Canonical (lowercase) id.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name identifying the batch (e.g. a filename or dataset name).</summary>
    public required string Name { get; init; }

    /// <summary>Batch category: <c>seed</c>, <c>userseed</c>, <c>import</c>, or <c>system</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Source URL for externally-sourced <c>seed</c>-type batches. <see langword="null"/> otherwise.</summary>
    public string? Url { get; init; }

    /// <summary>UTC timestamp when the batch was imported.</summary>
    public required string ImportedAt { get; init; }

    /// <summary>Id of the user who triggered the import. <see langword="null"/> for seeded batches.</summary>
    public string? ImportedById { get; init; }

    /// <summary>Number of records written in this batch.</summary>
    public int RecordCount { get; init; }

    /// <summary>The duplicate-resolution policy that was active for this batch.</summary>
    public required string ConflictPolicy { get; init; }

    /// <summary>Batch lifecycle state: <c>staged</c>, <c>applied</c>, or <c>discarded</c>.</summary>
    public required string Status { get; init; }

    /// <summary>UTC timestamp when the batch was applied. <see langword="null"/> while <see cref="Status"/> is <c>staged</c>.</summary>
    public string? AppliedAt { get; init; }
}
