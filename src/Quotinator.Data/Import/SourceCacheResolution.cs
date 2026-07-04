namespace Quotinator.Data.Import;

/// <summary>
/// The result of <see cref="ISourceCacheUpdater.ResolveAsync"/> — the effective batches to seed
/// from (with <c>downloadUrl</c> entries' <c>FilePath</c> resolved to a cached copy where one is
/// being used) plus a per-entry outcome summary.
/// </summary>
/// <param name="EffectiveBatches">The candidate batches, with resolved <c>FilePath</c>s substituted in for entries that have a cached copy in use.</param>
/// <param name="Results">One result per candidate entry that declared a <c>downloadUrl</c>.</param>
public sealed record SourceCacheResolution(
    IReadOnlyList<SeedBatch> EffectiveBatches,
    IReadOnlyList<SourceRefreshResult> Results);
