using Quotinator.Data.Import;

namespace Quotinator.Core.Models;

/// <summary>Response envelope for <c>GET /api/v1/admin/database/seed/preview</c>.</summary>
public sealed class SeedPreviewResponse
{
    /// <summary>One entry per source file in import order.</summary>
    public required IReadOnlyList<SeedFilePreviewResponse> Files { get; init; }

    /// <summary>One per-file report, computed by running the real import action planner read-only against the current database state (issue #221).</summary>
    public required IReadOnlyList<FileImportReport> Reports { get; init; }
}

/// <summary>Per-file summary within a <see cref="SeedPreviewResponse"/>.</summary>
public sealed class SeedFilePreviewResponse
{
    /// <summary>File name without directory path.</summary>
    public required string FileName { get; init; }

    /// <summary>Number of quote entries in this file.</summary>
    public required int QuoteCount { get; init; }

    /// <summary>The auto-update resolution outcome for this file (wire value, e.g. <c>"updated"</c>), or <c>null</c> for a file with no <c>downloadUrl</c>.</summary>
    public string? RefreshOutcome { get; init; }

    /// <summary>The effective file's own last-write time, or <c>null</c> when it has no <see cref="RefreshOutcome"/> or no trusted cache file exists.</summary>
    public DateTime? LastRefreshedAtUtc { get; init; }

    /// <summary>Non-<c>null</c> (wire value, e.g. <c>"missing"</c>) when the effective file could not be parsed at all.</summary>
    public string? Issue { get; init; }

    /// <summary>Localised message describing <see cref="Issue"/>, following <c>Accept-Language</c>. <c>null</c> when <see cref="Issue"/> is <c>null</c>.</summary>
    public string? Message { get; init; }
}
