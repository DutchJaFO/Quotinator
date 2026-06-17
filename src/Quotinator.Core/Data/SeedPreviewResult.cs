namespace Quotinator.Core.Data;

/// <summary>Summary of a dry-run file scan performed without touching the database.</summary>
/// <param name="Files">One entry per source file in import order, including its quote count.</param>
/// <param name="CrossFileDuplicates">Quotes with the same stable ID that appear in more than one file.</param>
/// <param name="TotalQuotes">Total quote rows across all files (including duplicates).</param>
/// <param name="UniqueQuotes">Unique quote IDs — what the database would contain after a full import with <c>skip</c> policy.</param>
public record SeedPreviewResult(
    IReadOnlyList<SeedFilePreview>     Files,
    IReadOnlyList<SeedDuplicateRecord> CrossFileDuplicates,
    int                                TotalQuotes,
    int                                UniqueQuotes);

/// <summary>Per-file summary within a <see cref="SeedPreviewResult"/>.</summary>
/// <param name="FileName">File name without directory path.</param>
/// <param name="QuoteCount">Number of quote entries in this file.</param>
public record SeedFilePreview(string FileName, int QuoteCount);
