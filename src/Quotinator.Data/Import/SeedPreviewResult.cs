namespace Quotinator.Data.Import;

/// <summary>
/// Summary of a dry-run file scan performed without writing anything to the database (#221).
/// </summary>
/// <param name="Files">One entry per source file in import order, including its quote count.</param>
/// <param name="Reports">
/// One <see cref="FileImportReport"/> per file, computed by running the real action planner against
/// the current database state (read-only — no action is ever staged or applied). Known limitation:
/// since nothing is actually written between files, a later file cannot see an earlier file's
/// hypothetical effect within this same preview call — a quote id appearing in two different files
/// that are both new to the database reports as <c>new</c> in both, not <c>new</c> + <c>modified</c>,
/// unlike a real seed run (where the earlier file's row is actually committed before the next file is
/// planned). Always accurate against a database that already has the relevant rows.
/// </param>
public record SeedPreviewResult(
    IReadOnlyList<SeedFilePreview>   Files,
    IReadOnlyList<FileImportReport>  Reports);

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
