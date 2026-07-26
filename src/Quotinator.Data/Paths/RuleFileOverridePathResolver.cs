using Quotinator.Data.Import;

namespace Quotinator.Data.Paths;

/// <inheritdoc/>
public sealed class RuleFileOverridePathResolver(
    string internalDownloadDir,
    string externalDownloadDir,
    string? bundledSourcesDir = null,
    string? importsDir = null) : IRuleFileOverridePathResolver
{
    /// <inheritdoc/>
    public string Resolve(string fileName, SeedBatchOrigin origin)
        => ResolveUnder(fileName, origin == SeedBatchOrigin.Bundled ? internalDownloadDir : externalDownloadDir, fileName);

    /// <inheritdoc/>
    public string ResolveBundledPath(string fileName, SeedBatchOrigin origin)
    {
        var baseDir = origin == SeedBatchOrigin.Bundled ? bundledSourcesDir : importsDir;
        if (baseDir is null)
            throw new InvalidOperationException($"No bundled directory was configured for origin '{origin}'.");

        return ResolveUnder(fileName, baseDir, fileName);
    }

    private static string ResolveUnder(string fileName, string baseDirRaw, string fileNameForError)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("A filename is required.", nameof(fileName));
        if (Path.GetFileName(fileName) != fileName || fileName is "." or "..")
            throw new ArgumentException($"'{fileNameForError}' must be a plain filename with no directory segments.", nameof(fileName));

        var baseDir  = Path.GetFullPath(baseDirRaw);
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, fileName));

        // Defence in depth beyond the plain-filename check above — the final resolved path must still
        // land inside baseDir regardless of how the check above might one day be bypassed or extended.
        if (!fullPath.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{fileNameForError}' resolves outside its target directory.", nameof(fileName));

        return fullPath;
    }
}
