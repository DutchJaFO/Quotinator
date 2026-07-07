namespace Quotinator.Core.Models;

/// <summary>
/// Response envelope for <c>POST /api/v1/import</c> and <c>.../import/preview</c> — identical
/// shape for both endpoints. <see cref="Preview"/> and <see cref="BatchId"/> are the only fields that
/// differ in practice: a preview run computes everything but rolls back, so nothing is persisted and
/// no batch exists to reference.
/// </summary>
public sealed class ImportResultResponse
{
    /// <summary>The created <c>ImportBatch</c> row's Id. <c>null</c> for a preview run — nothing is persisted.</summary>
    public Guid? BatchId { get; init; }

    /// <summary><c>true</c> when this result came from the preview endpoint (no writes were committed).</summary>
    public required bool Preview { get; init; }

    /// <summary>The duplicate-resolution policy actually applied to this import's quotes (wire value, e.g. <c>"newest-wins"</c>).</summary>
    public required string ConflictPolicy { get; init; }

    /// <summary>Row counts for the import.</summary>
    public required ImportSummary Summary { get; init; }

    /// <summary>Every conflict detected during this import, regardless of which policy resolved it.</summary>
    public IReadOnlyList<ImportConflictEntry> Conflicts { get; init; } = [];

    /// <summary>Rows that failed validation and were skipped without aborting the rest of the file.</summary>
    public IReadOnlyList<ImportRowError> Errors { get; init; } = [];
}

/// <summary>Row counts for an <see cref="ImportResultResponse"/>.</summary>
public sealed class ImportSummary
{
    /// <summary>Total rows read from the source file, including skipped/errored rows.</summary>
    public required int Total { get; init; }

    /// <summary>Rows written as brand-new quotes (no existing row with the same Id).</summary>
    public required int Imported { get; init; }

    /// <summary>Rows that matched an existing quote and were written via <c>newest-wins</c>/<c>merge-ours</c>/<c>merge-theirs</c>.</summary>
    public required int Updated { get; init; }

    /// <summary>Rows that matched an existing quote and were left unchanged (<c>skip</c>/<c>review</c>).</summary>
    public required int Skipped { get; init; }

    /// <summary>Rows that failed validation and were not written at all.</summary>
    public required int Errors { get; init; }
}

/// <summary>One detected conflict, mirroring the <c>System_ImportConflicts</c> row it produced.</summary>
public sealed class ImportConflictEntry
{
    /// <summary>Id of the quote involved in the conflict.</summary>
    public required string QuoteId { get; init; }

    /// <summary>The duplicate-resolution policy applied to this specific conflict (wire value).</summary>
    public required string AppliedPolicy { get; init; }

    /// <summary><c>"resolved"</c> or <c>"pending"</c> (only <c>review</c> conflicts are left pending).</summary>
    public required string Status { get; init; }

    /// <summary>Field values from the row already in the database before this import.</summary>
    public required IReadOnlyDictionary<string, object?> ExistingValue { get; init; }

    /// <summary>Field values from the incoming row being imported.</summary>
    public required IReadOnlyDictionary<string, object?> IncomingValue { get; init; }

    /// <summary>Per-field <c>"ours"</c>/<c>"theirs"</c> provenance — populated only for <c>merge-ours</c>/<c>merge-theirs</c> resolutions.</summary>
    public IReadOnlyDictionary<string, string>? MergedFields { get; init; }
}

/// <summary>One row that failed validation during an import, reported instead of aborting the rest of the file.</summary>
public sealed class ImportRowError
{
    /// <summary>1-based row number within the source file (header row, if any, is not counted).</summary>
    public required int Row { get; init; }

    /// <summary>Id of the affected quote, when one could be determined before the failure.</summary>
    public string? QuoteId { get; init; }

    /// <summary>Human-readable reason the row was rejected.</summary>
    public required string Message { get; init; }
}
