using Quotinator.Data.Import;

namespace Quotinator.Data.Paths;

/// <summary>
/// Resolves a caller-supplied <c>ruleFile</c>/<c>sourceAliasFile</c> filename to a safe, absolute path
/// under the persistent override directory for its origin (#153) — the same directories
/// <see cref="Import.ISourceCacheUpdater"/> already uses to cache a downloaded/refreshed copy of a
/// bundled source's main data file. Never resolves into the bundled/image sources directory itself —
/// that path is read-only in a real deployment (baked into the Docker image) and not on the
/// persistent volume, so a generated override is never written there.
/// </summary>
public interface IRuleFileOverridePathResolver
{
    /// <summary>
    /// Resolves <paramref name="fileName"/> to its override path for <paramref name="origin"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="fileName"/> is empty, contains a directory separator, or is exactly <c>".."</c> —
    /// this must always be a plain filename with no path segments.
    /// </exception>
    string Resolve(string fileName, SeedBatchOrigin origin);
}
