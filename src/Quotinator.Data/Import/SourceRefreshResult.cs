namespace Quotinator.Data.Import;

/// <summary>The outcome of resolving a single manifest entry with a <c>downloadUrl</c>.</summary>
/// <param name="Name">The source file's basename (e.g. <c>vilaboim_movie-quotes.json</c>).</param>
/// <param name="Url">The <c>downloadUrl</c> this entry was resolved from.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Detail">Optional human-readable detail (e.g. a collision's shared path, or a failure reason).</param>
public sealed record SourceRefreshResult(string Name, string Url, SourceRefreshOutcome Outcome, string? Detail = null);
