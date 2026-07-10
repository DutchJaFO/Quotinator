using Microsoft.Extensions.Logging;

namespace Quotinator.Data.Import;

/// <inheritdoc/>
public sealed class SourceCacheUpdater(
    IHttpClientFactory httpClientFactory,
    SourceCacheOptions options,
    ILogger<SourceCacheUpdater> logger) : ISourceCacheUpdater
{
    /// <summary>Name of the <see cref="IHttpClientFactory"/> client registered for this component, with its 5 s timeout configured at registration time.</summary>
    public const string HttpClientName = "SourceCacheUpdater";

    /// <inheritdoc/>
    public async Task<SourceCacheResolution> ResolveAsync(
        IReadOnlyList<SeedBatch> candidateBatches,
        bool allowNetwork,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        if (forceRefresh && !allowNetwork)
        {
            logger.LogInformation(
                "[Database - SourceRefresh] forceSourceRefresh requested but Quotinator__AutoUpdateSources is false — skipping network check");
        }

        // Flatten every (batch, file) pair that declares a downloadUrl, preserving order so the
        // effective batch list can be rebuilt by walking candidateBatches again afterward.
        var candidates = candidateBatches
            .SelectMany(batch => batch.Files.Select(file => (Batch: batch, File: file)))
            .Where(c => c.File.DownloadUrl is not null)
            .Select(c => (c.Batch, c.File, TargetPath: ResolveTargetPath(c.Batch, c.File)))
            .ToList();

        var collisionGroups = candidates
            .GroupBy(c => c.TargetPath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var (path, group) in collisionGroups)
        {
            var sources = string.Join(", ", group.Select(g => $"{Path.GetFileName(g.File.FilePath)} ({g.File.DownloadUrl})"));
            logger.LogError(
                "[Database - SourceRefresh] {Count} sources resolve to the same cache path {Path} — skipping all of them: {Sources}",
                group.Count, path, sources);
        }

        var results        = new List<SourceRefreshResult>();
        var effectivePaths = new Dictionary<(int BatchIndex, int FileIndex), string>();

        for (var batchIndex = 0; batchIndex < candidateBatches.Count; batchIndex++)
        {
            var batch = candidateBatches[batchIndex];
            for (var fileIndex = 0; fileIndex < batch.Files.Count; fileIndex++)
            {
                var file = batch.Files[fileIndex];
                if (file.DownloadUrl is null) continue;

                var targetPath = ResolveTargetPath(batch, file);
                var name       = Path.GetFileName(file.FilePath);

                if (collisionGroups.ContainsKey(targetPath))
                {
                    results.Add(new SourceRefreshResult(name, file.DownloadUrl, SourceRefreshOutcome.SkippedCollision, targetPath));
                    continue;
                }

                var (effectivePath, result) = await ResolveOneAsync(file, targetPath, batch.Origin, allowNetwork, forceRefresh, cancellationToken);
                effectivePaths[(batchIndex, fileIndex)] = effectivePath;
                results.Add(result);
            }
        }

        var effectiveBatches = candidateBatches
            .Select((batch, batchIndex) => batch with
            {
                Files = batch.Files
                    .Select((file, fileIndex) => effectivePaths.TryGetValue((batchIndex, fileIndex), out var effectivePath)
                        ? file with { FilePath = effectivePath }
                        : file)
                    .ToList()
            })
            .ToList();

        return new SourceCacheResolution(effectiveBatches, results);
    }

    private async Task<(string EffectivePath, SourceRefreshResult Result)> ResolveOneAsync(
        SeedFile file, string targetPath, SeedBatchOrigin origin, bool allowNetwork, bool forceRefresh, CancellationToken cancellationToken)
    {
        var name       = Path.GetFileName(file.FilePath);
        var cacheExists = File.Exists(targetPath);

        // Validating an existing cache hit (not just a freshly downloaded file) means a cache file
        // corrupted before this validation existed — or corrupted by any future bug — self-heals on
        // the next access, rather than being silently trusted forever just because it's not expired.
        var cacheValid = cacheExists && IsCachedContentValid(targetPath, name);

        if (!allowNetwork)
        {
            return cacheValid
                ? (targetPath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.UpToDate, LastRefreshedAtUtc: GetLastRefreshedAt(targetPath)))
                : (file.FilePath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.UpToDate));
        }

        var needsRefresh = forceRefresh || !cacheValid || IsStale(targetPath, file);
        if (!needsRefresh)
            return (targetPath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.UpToDate, LastRefreshedAtUtc: GetLastRefreshedAt(targetPath)));

        var downloaded = await TryDownloadAndPrepareAsync(file, targetPath, origin, cancellationToken);
        if (downloaded)
            return (targetPath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.Updated, LastRefreshedAtUtc: GetLastRefreshedAt(targetPath)));

        // Failed — fall back to the cached copy if one exists and is valid (even if stale), else the original file.
        return cacheValid
            ? (targetPath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.Failed, LastRefreshedAtUtc: GetLastRefreshedAt(targetPath)))
            : (file.FilePath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.Failed));
    }

    private static DateTime? GetLastRefreshedAt(string path)
        => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;

    private bool IsCachedContentValid(string path, string name)
    {
        if (options.ValidateCanonicalSchema is null) return true;

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Database - SourceRefresh] cached copy of {File} could not be read — treating as invalid", name);
            return false;
        }

        if (options.ValidateCanonicalSchema(content)) return true;

        logger.LogWarning("[Database - SourceRefresh] cached copy of {File} failed canonical-schema validation — treating as invalid", name);
        return false;
    }

    private bool IsStale(string cachedPath, SeedFile file)
    {
        var ttlHours = file.RefreshIntervalHours ?? options.DefaultRefreshIntervalHours;
        var age      = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachedPath);
        return age >= TimeSpan.FromHours(ttlHours);
    }

    private async Task<bool> TryDownloadAndPrepareAsync(SeedFile file, string targetPath, SeedBatchOrigin origin, CancellationToken cancellationToken)
    {
        var name         = Path.GetFileName(file.FilePath);
        var rawTempPath  = targetPath + ".download.tmp";
        var convertedTempPath = targetPath + ".converted.tmp";

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(file.DownloadUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "[Database - SourceRefresh] could not reach {Url} ({Status}) — using local {File}",
                    file.DownloadUrl, (int)response.StatusCode, name);
                return false;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            // Downloaded content is written to its own temp file first, kept separate from any
            // conversion output — so a conversion failure leaves the raw download inspectable rather
            // than risking a converter partially overwriting its own input mid-read.
            await File.WriteAllBytesAsync(rawTempPath, bytes, cancellationToken);

            var preparedPath = rawTempPath;

            if (file.Converter is not null)
            {
                var converter = options.Converters?.GetValueOrDefault(file.Converter);
                if (converter is null)
                {
                    logger.LogWarning(
                        "[Database - SourceRefresh] converter '{Converter}' named for {File} is not registered in this build — using local {File}",
                        file.Converter, name, name);
                    return false;
                }

                if (converter.IsInternalOnly && origin == SeedBatchOrigin.UserImports)
                {
                    logger.LogWarning(
                        "[Database - SourceRefresh] converter '{Converter}' named for {File} is internal-only and cannot be selected from a user-writable manifest — using local {File}",
                        file.Converter, name, name);
                    return false;
                }

                try
                {
                    await converter.ConvertAsync(rawTempPath, convertedTempPath, file.ConverterOptions, cancellationToken);
                    preparedPath = convertedTempPath;
                }
                catch (SourceConversionException ex)
                {
                    logger.LogWarning(ex,
                        "[Database - SourceRefresh] conversion of {File} via '{Converter}' failed — using local {File}",
                        name, file.Converter, name);
                    return false;
                }
            }

            // Validation runs regardless of whether a converter ran — a source with no converter but
            // whose downloadUrl serves raw, non-canonical content is exactly the failure mode this
            // closes: fails validation here instead of silently corrupting the cache.
            if (options.ValidateCanonicalSchema is not null)
            {
                var content = await File.ReadAllTextAsync(preparedPath, cancellationToken);
                if (!options.ValidateCanonicalSchema(content))
                {
                    logger.LogWarning(
                        "[Database - SourceRefresh] {Stage} content for {File} failed canonical-schema validation — using local {File}",
                        file.Converter is not null ? "converted" : "downloaded", name, name);
                    return false;
                }
            }

            // Atomic rename on the same volume — an interrupted move never leaves a half-written
            // cache file behind for the next seed operation to read.
            File.Move(preparedPath, targetPath, overwrite: true);

            logger.LogInformation("[Database - SourceRefresh] updated {File} from {Url}", name, file.DownloadUrl);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Database - SourceRefresh] could not reach {Url} — using local {File}", file.DownloadUrl, name);
            return false;
        }
        finally
        {
            TryDeleteFile(rawTempPath);
            TryDeleteFile(convertedTempPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort temp file cleanup — a leftover .tmp file is harmless and gets overwritten
            // by the next attempt; never let cleanup failure mask the real outcome.
        }
    }

    private string ResolveTargetPath(SeedBatch batch, SeedFile file)
    {
        var target = file.DownloadTarget ?? (batch.Origin == SeedBatchOrigin.Bundled ? DownloadTarget.Internal : DownloadTarget.External);
        var dir    = target == DownloadTarget.Internal ? options.InternalDownloadDir : options.ExternalDownloadDir;
        return Path.Combine(dir, Path.GetFileName(file.FilePath));
    }
}
