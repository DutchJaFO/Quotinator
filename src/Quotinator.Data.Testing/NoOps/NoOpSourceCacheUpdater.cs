using Quotinator.Data.Import;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="ISourceCacheUpdater"/> for use in tests that do not exercise the auto-update source cache feature — resolves every candidate batch unchanged, with no network calls.</summary>
public sealed class NoOpSourceCacheUpdater : ISourceCacheUpdater
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpSourceCacheUpdater Instance = new();

    /// <inheritdoc/>
    public Task<SourceCacheResolution> ResolveAsync(
        IReadOnlyList<SeedBatch> candidateBatches,
        bool allowNetwork,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new SourceCacheResolution(candidateBatches, []));
}
