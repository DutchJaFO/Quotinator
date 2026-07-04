namespace Quotinator.Data.Import;

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
/// <param name="RefreshOutcome">
/// The auto-update resolution outcome for this file (see <see cref="SourceRefreshOutcome"/>), or
/// <c>null</c> for a file with no <c>downloadUrl</c> — it was never a candidate for the cache
/// resolution pass at all. A non-null value here is what makes an outwardly-normal
/// <see cref="QuoteCount"/> (e.g. <c>0</c> from a source that fell back to its original bundled file)
/// distinguishable from a source that was never expected to have any content.
/// </param>
/// <param name="LastRefreshedAtUtc">The effective file's own last-write time, or <c>null</c> when it has no <see cref="RefreshOutcome"/> or no trusted cache file exists.</param>
/// <param name="Issue">
/// Non-<c>null</c> when the effective file could not be parsed at all — the only way to distinguish
/// a <see cref="QuoteCount"/> of <c>0</c> caused by a genuine parse failure from a file that is
/// simply, validly empty. Applies to every file, not only those with a <c>downloadUrl</c> — a
/// local/curated/user-import file can be malformed too. The API layer maps this to a localised
/// message via <c>IApiLocalizer</c> — this type itself carries no message text.
/// </param>
public record SeedFilePreview(string FileName, int QuoteCount, SourceRefreshOutcome? RefreshOutcome = null, DateTime? LastRefreshedAtUtc = null, SeedFileIssue? Issue = null);
