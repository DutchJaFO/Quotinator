namespace Quotinator.Data.Import;

/// <summary>What happened when a single manifest entry with a <c>downloadUrl</c> was resolved.</summary>
public enum SourceRefreshOutcome
{
    /// <summary>A stale, missing, or force-requested cache was successfully downloaded and overwritten.</summary>
    Updated,

    /// <summary>An existing cached copy was already fresh enough (or the network is disabled) — used as-is, no download attempted.</summary>
    UpToDate,

    /// <summary>A download was attempted and failed (unreachable, timeout, non-success status) — fell back to the most recent available copy.</summary>
    Failed,

    /// <summary>This entry's resolved cache path is claimed by more than one distinct source — skipped entirely, falls back to the original bundled/local file.</summary>
    SkippedCollision
}
