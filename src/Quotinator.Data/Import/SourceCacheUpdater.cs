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

                var (effectivePath, result) = await ResolveOneAsync(file, targetPath, allowNetwork, forceRefresh, cancellationToken);
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
        SeedFile file, string targetPath, bool allowNetwork, bool forceRefresh, CancellationToken cancellationToken)
    {
        var name       = Path.GetFileName(file.FilePath);
        var cacheExists = File.Exists(targetPath);

        if (!allowNetwork)
        {
            return cacheExists
                ? (targetPath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.UpToDate))
                : (file.FilePath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.UpToDate));
        }

        var needsRefresh = forceRefresh || !cacheExists || IsStale(targetPath, file);
        if (!needsRefresh)
            return (targetPath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.UpToDate));

        var downloaded = await TryDownloadAsync(file, targetPath, cancellationToken);
        if (downloaded)
            return (targetPath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.Updated));

        // Failed — fall back to the cached copy if one exists (even stale), else the original file.
        return cacheExists
            ? (targetPath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.Failed))
            : (file.FilePath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.Failed));
    }

    private bool IsStale(string cachedPath, SeedFile file)
    {
        var ttlHours = file.RefreshIntervalHours ?? options.DefaultRefreshIntervalHours;
        var age      = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachedPath);
        return age >= TimeSpan.FromHours(ttlHours);
    }

    private async Task<bool> TryDownloadAsync(SeedFile file, string targetPath, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(file.FilePath);
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
            // Write to a temp file in the same directory then move into place — an atomic
            // rename on the same volume, so an interrupted download never leaves a half-written
            // cache file behind for the next seed operation to read.
            var tempPath = targetPath + ".tmp";
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
            File.Move(tempPath, targetPath, overwrite: true);

            logger.LogInformation("[Database - SourceRefresh] updated {File} from {Url}", name, file.DownloadUrl);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Database - SourceRefresh] could not reach {Url} — using local {File}", file.DownloadUrl, name);
            return false;
        }
    }

    private string ResolveTargetPath(SeedBatch batch, SeedFile file)
    {
        var target = file.DownloadTarget ?? (batch.Origin == SeedBatchOrigin.Bundled ? DownloadTarget.Internal : DownloadTarget.External);
        var dir    = target == DownloadTarget.Internal ? options.InternalDownloadDir : options.ExternalDownloadDir;
        return Path.Combine(dir, Path.GetFileName(file.FilePath));
    }
}
