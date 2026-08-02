using Quotinator.Data.Enums;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Quotinator.Data.Helpers;
using Quotinator.Data.Paths;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Import;

/// <summary>
/// Chooses between a bundled/user-imported source's own <c>ruleFile</c>/<c>sourceAliasFile</c> and a
/// generated override for it (#153) — shared by the seeding pipeline (which must pick the right file
/// to actually load) and the rule-file endpoints (which must read/write the same effective content the
/// seeding pipeline would use, so "what you see is what gets applied"). A static helper rather than an
/// instance service, since both callers already hold their own <see cref="IRuleFileOverridePathResolver"/>/
/// <see cref="ISourceFileOverrideRegistry"/> instances and there is no other state to own.
/// </summary>
public static class EffectiveRuleFileResolver
{
    /// <summary>
    /// Resolves <paramref name="bundledPath"/> to the path that should actually be read: a registered,
    /// hash-verified override when one exists, otherwise <paramref name="bundledPath"/> unchanged. An
    /// override file present on disk without a matching registration (deleted registration, hand-edited
    /// content, a stale copy from a previous version) is never silently trusted.
    /// </summary>
    public static async Task<string> ResolveEffectivePathAsync(
        string bundledPath,
        SeedBatchOrigin origin,
        IRuleFileOverridePathResolver pathResolver,
        ISourceFileOverrideRegistry registry,
        ILogger logger,
        CancellationToken cancellationToken = default,
        string logPrefix = "[Database - Seed]")
    {
        var fileName = Path.GetFileName(bundledPath);

        string overridePath;
        try
        {
            overridePath = pathResolver.Resolve(fileName, origin);
        }
        catch (ArgumentException)
        {
            // Should never happen for a manifest-derived filename (always a plain filename by
            // construction) — fail open to the bundled path rather than block seeding over it.
            return bundledPath;
        }

        if (!File.Exists(overridePath)) return bundledPath;

        var registered = await registry.FindAsync(fileName, origin, cancellationToken);
        if (registered is null)
        {
            logger.LogWarning("{Prefix} {File} has an override file on disk but no registered entry — ignoring it and using the bundled copy",
                logPrefix, LogSanitizer.ForLog(fileName));
            return bundledPath;
        }

        var actualHash = ComputeContentHash(await File.ReadAllTextAsync(overridePath, cancellationToken));
        if (!string.Equals(actualHash, registered.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("{Prefix} {File}'s override content hash no longer matches its registration — ignoring it and using the bundled copy",
                logPrefix, LogSanitizer.ForLog(fileName));
            return bundledPath;
        }

        return overridePath;
    }

    /// <summary>SHA-256 of <paramref name="content"/>, lowercase hex — the same hash shape <see cref="ISourceFileOverrideRegistry"/> stores.</summary>
    public static string ComputeContentHash(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    /// <summary>
    /// Reads the current effective content for <paramref name="fileName"/>/<paramref name="origin"/> —
    /// a registered, hash-verified override when one exists, otherwise the bundled/image copy — for use
    /// as the merge base when generating a new override (#153's endpoints). Returns <see langword="null"/>
    /// when neither exists (no rule file has ever been authored for this source yet).
    /// </summary>
    public static async Task<string?> ReadEffectiveContentAsync(
        string fileName,
        SeedBatchOrigin origin,
        IRuleFileOverridePathResolver pathResolver,
        ISourceFileOverrideRegistry registry,
        ILogger logger,
        CancellationToken cancellationToken = default,
        string logPrefix = "[Database - Seed]")
    {
        string bundledPath;
        try
        {
            bundledPath = pathResolver.ResolveBundledPath(fileName, origin);
        }
        catch (InvalidOperationException)
        {
            bundledPath = string.Empty;
        }

        var effectivePath = await ResolveEffectivePathAsync(
            bundledPath.Length > 0 ? bundledPath : fileName, origin, pathResolver, registry, logger, cancellationToken, logPrefix);

        return File.Exists(effectivePath) ? await File.ReadAllTextAsync(effectivePath, cancellationToken) : null;
    }
}
