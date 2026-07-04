namespace Quotinator.Data.Import;

/// <summary>Configuration for <see cref="ISourceCacheUpdater"/>.</summary>
/// <param name="InternalDownloadDir">Default cache directory for bundled-manifest entries — <c>{dataDir}/sources/download/</c>.</param>
/// <param name="ExternalDownloadDir">Default cache directory for user-imports-manifest entries — <c>{dataDir}/imports/download/</c>.</param>
/// <param name="DefaultRefreshIntervalHours">Global fallback TTL, used when a manifest entry has no <c>refreshIntervalHours</c> override.</param>
public sealed record SourceCacheOptions(
    string InternalDownloadDir,
    string ExternalDownloadDir,
    int DefaultRefreshIntervalHours);
