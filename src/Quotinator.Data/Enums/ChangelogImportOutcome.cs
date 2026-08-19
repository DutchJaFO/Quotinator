namespace Quotinator.Data.Enums;

/// <summary>
/// How the startup changelog import concluded, as observed by a reader that found the database empty
/// and had to decide whether that emptiness is meaningful (#309). Never persisted — this is in-process
/// coordination state only, so it carries no <c>CHECK</c> constraint and no migration.
/// </summary>
public enum ChangelogImportOutcome
{
    /// <summary>
    /// The import ran to completion. The changelog database is authoritative from this point on,
    /// <em>including when it holds no entries at all</em> — a new application legitimately has no
    /// changelog yet, which is an answer rather than a failure.
    /// </summary>
    Succeeded,

    /// <summary>The import threw. The database cannot be trusted to hold current content, so a reader falls back to the JSON files.</summary>
    Failed,

    /// <summary>
    /// The import had still not concluded when a waiting reader's budget expired. Produced only by
    /// <see cref="Quotinator.Data.Import.IChangelogImportReadiness.WaitAsync"/>, never by the importer
    /// itself — it describes the wait, not the import, and is kept distinct from
    /// <see cref="Failed"/> so "we stopped waiting" is never reported as "the import broke".
    /// </summary>
    TimedOut
}
