namespace Quotinator.Data.Import;

/// <summary>The outcome of resolving a single manifest entry with a <c>downloadUrl</c>.</summary>
/// <param name="Name">The source file's basename (e.g. <c>vilaboim_movie-quotes.json</c>).</param>
/// <param name="Url">The <c>downloadUrl</c> this entry was resolved from.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Detail">Optional human-readable detail (e.g. a collision's shared path, or a failure reason).</param>
/// <param name="LastRefreshedAtUtc">
/// The effective cache file's own last-write time, or <c>null</c> when no trusted cache file exists
/// (e.g. falling back to the original bundled/local file, or a collision). This is the file's actual
/// mtime, not "now" — so an <see cref="SourceRefreshOutcome.UpToDate"/> result still reports how old
/// the cached copy actually is, not just that it was within the TTL window.
/// </param>
public sealed record SourceRefreshResult(
    string Name,
    string Url,
    SourceRefreshOutcome Outcome,
    string? Detail = null,
    DateTime? LastRefreshedAtUtc = null);
