namespace Quotinator.Data.Import;

/// <summary>Configuration for <see cref="ISourceCacheUpdater"/>.</summary>
/// <param name="InternalDownloadDir">Default cache directory for bundled-manifest entries — <c>{dataDir}/sources/download/</c>.</param>
/// <param name="ExternalDownloadDir">Default cache directory for user-imports-manifest entries — <c>{dataDir}/imports/download/</c>.</param>
/// <param name="DefaultRefreshIntervalHours">Global fallback TTL, used when a manifest entry has no <c>refreshIntervalHours</c> override.</param>
/// <param name="Converters">Registry of compiled converter plugins, keyed by <see cref="IQuoteSourceConverter.Name"/> (case-insensitive). <c>null</c> means no converters are wired up — any entry declaring a <c>converter</c> fails closed.</param>
/// <param name="ValidateCanonicalSchema">
/// Validates that downloaded/converted file content actually deserializes as Quotinator's canonical
/// quote schema, before it is accepted into the cache. <c>null</c> means validation is skipped
/// entirely (feature not wired up) — this is not the same as always accepting; see
/// <see cref="SourceCacheUpdater"/> for exactly where this is invoked. Deliberately schema-agnostic at
/// this layer (a plain <see cref="Func{T,TResult}"/> over file content) since real validation requires
/// <c>Quotinator.Core</c>'s <c>SourceQuote</c>, and <c>Quotinator.Data</c> must not depend on
/// <c>Quotinator.Core</c> — the composition root builds and injects this delegate instead.
/// </param>
public sealed record SourceCacheOptions(
    string InternalDownloadDir,
    string ExternalDownloadDir,
    int DefaultRefreshIntervalHours,
    IReadOnlyDictionary<string, IQuoteSourceConverter>? Converters = null,
    Func<string, bool>? ValidateCanonicalSchema = null);
