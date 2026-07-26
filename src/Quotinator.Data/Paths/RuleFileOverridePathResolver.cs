using Quotinator.Data.Import;

namespace Quotinator.Data.Paths;

/// <inheritdoc/>
public sealed class RuleFileOverridePathResolver(string internalDownloadDir, string externalDownloadDir) : IRuleFileOverridePathResolver
{
    /// <inheritdoc/>
    public string Resolve(string fileName, SeedBatchOrigin origin)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("A filename is required.", nameof(fileName));
        if (Path.GetFileName(fileName) != fileName || fileName is "." or "..")
            throw new ArgumentException($"'{fileName}' must be a plain filename with no directory segments.", nameof(fileName));

        var baseDir  = Path.GetFullPath(origin == SeedBatchOrigin.Bundled ? internalDownloadDir : externalDownloadDir);
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, fileName));

        // Defence in depth beyond the plain-filename check above — the final resolved path must still
        // land inside baseDir regardless of how the check above might one day be bypassed or extended.
        if (!fullPath.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{fileName}' resolves outside its target directory.", nameof(fileName));

        return fullPath;
    }
}
