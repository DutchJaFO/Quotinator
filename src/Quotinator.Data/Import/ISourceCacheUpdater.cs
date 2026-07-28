namespace Quotinator.Data.Import;

/// <summary>
/// Resolves the effective files to seed from when any candidate <see cref="SeedBatch"/> entries
/// declare a <c>downloadUrl</c>/<c>github</c> — refreshing a cached copy from the network first
/// when it's missing, stale, or a refresh is forced, and detecting collisions where more than one
/// source would resolve to the same on-disk cache path. Never throws for a network failure or a
/// collision — always falls back to the safest available file for that entry.
/// </summary>
public interface ISourceCacheUpdater
{
    /// <summary>
    /// Resolves <paramref name="candidateBatches"/> to their effective form for this seed
    /// operation.
    /// </summary>
    /// <param name="candidateBatches">The full candidate batch list (bundled and user-imports together) — unchanged, reused across calls.</param>
    /// <param name="allowNetwork">When <c>false</c> (<c>Quotinator__AutoUpdateSources=false</c>), no network call is ever attempted — resolves to an existing cached copy if present, else the original bundled/local file.</param>
    /// <param name="forceRefresh">When <c>true</c> and <paramref name="allowNetwork"/> is also <c>true</c>, bypasses the TTL check for every candidate entry. Has no effect when <paramref name="allowNetwork"/> is <c>false</c> — an explicit no-network declaration is never overridden by a force flag.</param>
    /// <param name="cancellationToken">Cancellation token for the underlying HTTP requests.</param>
    Task<SourceCacheResolution> ResolveAsync(
        IReadOnlyList<SeedBatch> candidateBatches,
        bool allowNetwork,
        bool forceRefresh,
        CancellationToken cancellationToken = default);
}
