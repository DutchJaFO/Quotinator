using Microsoft.Extensions.Logging;
using Quotinator.Data.Enums;
using Quotinator.Data.Logging;

namespace Quotinator.Data.Import;

/// <inheritdoc/>
public sealed class SourceCacheUpdater(
    IHttpClientFactory httpClientFactory,
    SourceCacheOptions options,
    ILogger<SourceCacheUpdater> logger) : ISourceCacheUpdater
{
    /// <summary>Name of the <see cref="IHttpClientFactory"/> client registered for this component. Its timeout is configured at registration time, overridable via <c>Quotinator:SourceRefreshTimeoutSeconds</c> — see <see cref="DefaultHttpTimeoutSeconds"/>.</summary>
    public const string HttpClientName = "SourceCacheUpdater";

    /// <summary>
    /// Default <see cref="HttpClientName"/> timeout in seconds, used when <c>Quotinator:SourceRefreshTimeoutSeconds</c>
    /// is not set. A slow/unreachable upstream must never block startup, reseed, or reset indefinitely — the
    /// updater always falls back to the existing cached/local file on timeout. 30 s (raised from 5 s, 2026-08-09):
    /// a cold HttpClient's first request (fresh DNS + TCP + TLS) can legitimately exceed 5 s even against a
    /// healthy endpoint, which was tripping the fallback path more often than a genuinely unreachable upstream
    /// warranted.
    /// <para>
    /// 90 s (raised from 30 s, 2026-08-20) to stay above <see cref="DefaultConnectTimeoutSeconds"/>. If the
    /// request budget were at or below the connect budget, the request would cancel first and the connect
    /// budget would never apply — reintroducing exactly the defect #323 fixed, where a stalled connect was
    /// bounded by whichever request happened to be waiting on it. The margin above connect is what covers
    /// the transfer itself.
    /// </para>
    /// </summary>
    public const int DefaultHttpTimeoutSeconds = 90;

    /// <summary>
    /// Default connect budget in seconds for <see cref="HttpClientName"/>'s primary handler, used when
    /// <c>Quotinator:SourceRefreshConnectTimeoutSeconds</c> is not set (#323).
    /// <para>
    /// <see cref="System.Net.Http.SocketsHttpHandler.ConnectTimeout"/> defaults to
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>, which means a stalled connect or TLS
    /// handshake has no budget of its own — it is bounded only by whichever request happens to be
    /// waiting on it, and the attempt is not cancelled when that request gives up. Measured live
    /// 2026-08-17: two sources each burned the full <see cref="DefaultHttpTimeoutSeconds"/> window
    /// waiting on a connection that was never established, adding ~70 s to startup.
    /// </para>
    /// <para>
    /// Kept deliberately under <see cref="DefaultHttpTimeoutSeconds"/>: the request budget has to cover
    /// connect *plus* transfer, so a connect budget at parity would leave nothing for the download
    /// itself. That relationship is the invariant — the specific numbers are not.
    /// </para>
    /// <para>
    /// 60 s (raised from 10 s, 2026-08-20). The original 10 s was chosen only as a safe finite value
    /// when the real defect was an infinite budget; it was never measured as correct. Raising it costs
    /// nothing user-visible — a refresh that is still connecting happens behind the startup wait page
    /// (#280), which already tells the user work is in progress — while a marginal or slow link now has
    /// a realistic chance of completing instead of being abandoned. Retry behaviour is #329's, and
    /// these values are expected to be tuned alongside it once that lands.
    /// </para>
    /// </summary>
    public const int DefaultConnectTimeoutSeconds = 60;

    /// <summary>
    /// Default pooled-connection lifetime in minutes for <see cref="HttpClientName"/>'s primary handler
    /// (#323).
    /// <para>
    /// <see cref="System.Net.Http.SocketsHttpHandler.PooledConnectionLifetime"/> also defaults to
    /// infinite. <c>IHttpClientFactory</c>'s own handler lifetime recycles the *handler* but never the
    /// connections already pooled inside it, so without this a connection never rotates and a DNS
    /// change on the upstream host is never observed. 2 minutes matches the factory's default handler
    /// lifetime, so the two expire on the same cadence rather than one outliving the other.
    /// </para>
    /// </summary>
    public const int DefaultPooledConnectionLifetimeMinutes = 2;

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
        List<(SeedBatch Batch, SeedFile File, string TargetPath)> candidates =
        [
            .. candidateBatches
                .SelectMany(batch => batch.Files.Select(file => (Batch: batch, File: file)))
                .Where(c => c.File.DownloadUrl is not null)
                .Select(c => (c.Batch, c.File, TargetPath: ResolveTargetPath(c.Batch, c.File)))
        ];

        Dictionary<string, List<(SeedBatch Batch, SeedFile File, string TargetPath)>> collisionGroups = candidates
            .GroupBy(c => c.TargetPath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach ((string? path, List<(SeedBatch Batch, SeedFile File, string TargetPath)>? group) in collisionGroups)
        {
            string sources = string.Join(", ", group.Select(g => $"{Path.GetFileName(g.File.FilePath)} ({g.File.DownloadUrl})"));
            logger.LogError(
                "[Database - SourceRefresh] {Count} sources resolve to the same cache path {Path} — skipping all of them: {Sources}",
                group.Count, path, sources);
        }

        List<SourceRefreshResult> results                                 = [];
        Dictionary<(int BatchIndex, int FileIndex), string> effectivePaths = [];

        for (int batchIndex = 0; batchIndex < candidateBatches.Count; batchIndex++)
        {
            SeedBatch batch = candidateBatches[batchIndex];
            for (int fileIndex = 0; fileIndex < batch.Files.Count; fileIndex++)
            {
                SeedFile file = batch.Files[fileIndex];
                if (file.DownloadUrl is null) continue;

                string targetPath = ResolveTargetPath(batch, file);
                string name       = Path.GetFileName(file.FilePath);

                if (collisionGroups.ContainsKey(targetPath))
                {
                    results.Add(new SourceRefreshResult(name, file.DownloadUrl, SourceRefreshOutcome.SkippedCollision, targetPath));
                    continue;
                }

                (string? effectivePath, SourceRefreshResult? result) = await ResolveOneAsync(file, targetPath, batch.Origin, allowNetwork, forceRefresh, cancellationToken);
                effectivePaths[(batchIndex, fileIndex)] = effectivePath;
                results.Add(result);
            }
        }

        List<SeedBatch> effectiveBatches =
        [
            .. candidateBatches
                .Select((batch, batchIndex) => batch with
                {
                    Files = [.. batch.Files
                        .Select((file, fileIndex) => effectivePaths.TryGetValue((batchIndex, fileIndex), out string? effectivePath)
                            ? file with { FilePath = effectivePath }
                            : file)]
                })
        ];

        return new SourceCacheResolution(effectiveBatches, results);
    }

    private async Task<(string EffectivePath, SourceRefreshResult Result)> ResolveOneAsync(
        SeedFile file, string targetPath, SeedBatchOrigin origin, bool allowNetwork, bool forceRefresh, CancellationToken cancellationToken)
    {
        string name       = Path.GetFileName(file.FilePath);
        bool cacheExists = File.Exists(targetPath);

        // Validating an existing cache hit (not just a freshly downloaded file) means a cache file
        // corrupted before this validation existed — or corrupted by any future bug — self-heals on
        // the next access, rather than being silently trusted forever just because it's not expired.
        bool cacheValid = cacheExists && IsCachedContentValid(targetPath, name);

        if (!allowNetwork)
        {
            return cacheValid
                ? (targetPath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.UpToDate, LastRefreshedAtUtc: GetLastRefreshedAt(targetPath)))
                : (file.FilePath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.UpToDate));
        }

        bool needsRefresh = forceRefresh || !cacheValid || IsStale(targetPath, file);
        if (!needsRefresh)
            return (targetPath, new SourceRefreshResult(name, file.DownloadUrl!, SourceRefreshOutcome.UpToDate, LastRefreshedAtUtc: GetLastRefreshedAt(targetPath)));

        bool downloaded = await TryDownloadAndPrepareAsync(file, targetPath, origin, cancellationToken);
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
        int ttlHours = file.RefreshIntervalHours ?? options.DefaultRefreshIntervalHours;
        TimeSpan age      = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachedPath);
        return age >= TimeSpan.FromHours(ttlHours);
    }

    private async Task<bool> TryDownloadAndPrepareAsync(SeedFile file, string targetPath, SeedBatchOrigin origin, CancellationToken cancellationToken)
    {
        string name         = Path.GetFileName(file.FilePath);
        string rawTempPath  = targetPath + ".download.tmp";
        string convertedTempPath = targetPath + ".converted.tmp";

        try
        {
            HttpClient client = httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response = await client.GetAsync(file.DownloadUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "[Database - SourceRefresh] could not reach {Url} ({Status}) — using local {File}",
                    file.DownloadUrl, (int)response.StatusCode, name);
                return false;
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            // Downloaded content is written to its own temp file first, kept separate from any
            // conversion output — so a conversion failure leaves the raw download inspectable rather
            // than risking a converter partially overwriting its own input mid-read.
            await File.WriteAllBytesAsync(rawTempPath, bytes, cancellationToken);

            string preparedPath = rawTempPath;

            if (file.Converter is not null)
            {
                IQuoteSourceConverter? converter = options.Converters?.GetValueOrDefault(file.Converter);
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
                string content = await File.ReadAllTextAsync(preparedPath, cancellationToken);
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

            logger.LogSourceRefreshUpdated(name, file.DownloadUrl);
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
        DownloadTarget target = file.DownloadTarget ?? (batch.Origin == SeedBatchOrigin.Bundled ? DownloadTarget.Internal : DownloadTarget.External);
        string dir    = target == DownloadTarget.Internal ? options.InternalDownloadDir : options.ExternalDownloadDir;
        return Path.Combine(dir, Path.GetFileName(file.FilePath));
    }
}
