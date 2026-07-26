using Quotinator.Data.Entities;
using Quotinator.Data.Import;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Tracks which bundled/user-imported source rule files (#153) currently have a generated override
/// on the persistent volume, and what its content hash was at registration time — so the seeding
/// pipeline can know for certain an override is genuinely one this project's own generation mechanism
/// produced, rather than inferring it from file existence alone.
/// </summary>
public interface ISourceFileOverrideRegistry
{
    /// <summary>The registered override for this exact (fileName, origin) pair, if one exists.</summary>
    Task<SourceFileOverride?> FindAsync(string fileName, SeedBatchOrigin origin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers or updates the override for (fileName, origin) — an upsert keyed by that pair, not a
    /// history log. A prior registration's <c>ContentHash</c>/<c>SourceBatchId</c> are overwritten;
    /// only the current state is kept.
    /// </summary>
    Task RegisterAsync(string fileName, SeedBatchOrigin origin, string contentHash, string? sourceBatchId, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the registered override for (fileName, origin), if one exists. Returns <see langword="false"/> when there was nothing to remove.</summary>
    Task<bool> RemoveAsync(string fileName, SeedBatchOrigin origin, CancellationToken cancellationToken = default);
}
