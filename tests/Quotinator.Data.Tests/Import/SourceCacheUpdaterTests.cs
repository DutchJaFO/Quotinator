using System.Net;
using Microsoft.Extensions.Logging;
using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

/// <summary>
/// Covers Verification rows 1-12 of the #140 plan doc
/// (docs/milestones/data-import-sources/140-auto-update-sources-plan.md).
/// </summary>
[TestClass]
public class SourceCacheUpdaterTests
{
    private string _tempDir     = null!;
    private string _internalDir = null!;
    private string _externalDir = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir     = Directory.CreateTempSubdirectory("quotinator_sourcecache_").FullName;
        _internalDir = Path.Combine(_tempDir, "sources", "download");
        _externalDir = Path.Combine(_tempDir, "imports", "download");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // Row 1: AutoUpdateSources=false skips all network checks; seeds from cached-if-present else the original file.
    [TestMethod]
    public async Task ResolveAsync_AllowNetworkFalse_NoCacheYet_UsesOriginalFile()
    {
        var file  = new SeedFile("/bundled/a.json", null, "https://example.com/a.json");
        var batch = Batch(SeedBatchOrigin.Bundled, file);
        var updater = CreateUpdater(NeverCallNetwork());

        var result = await updater.ResolveAsync([batch], allowNetwork: false, forceRefresh: false);

        Assert.AreEqual("/bundled/a.json", result.EffectiveBatches[0].Files[0].FilePath);
        Assert.AreEqual(SourceRefreshOutcome.UpToDate, result.Results.Single().Outcome);
    }

    [TestMethod]
    public async Task ResolveAsync_AllowNetworkFalse_CacheExists_UsesCachedCopy()
    {
        var cachedPath = WriteCachedFile(_internalDir, "a.json", "cached", DateTime.UtcNow);
        var file  = new SeedFile("/bundled/a.json", null, "https://example.com/a.json");
        var batch = Batch(SeedBatchOrigin.Bundled, file);
        var updater = CreateUpdater(NeverCallNetwork());

        var result = await updater.ResolveAsync([batch], allowNetwork: false, forceRefresh: false);

        Assert.AreEqual(cachedPath, result.EffectiveBatches[0].Files[0].FilePath);
        Assert.AreEqual(SourceRefreshOutcome.UpToDate, result.Results.Single().Outcome);
    }

    // Row 2: fresh cached copy (within TTL) is used without a network call.
    [TestMethod]
    public async Task ResolveAsync_FreshCache_DoesNotCallNetwork()
    {
        var cachedPath = WriteCachedFile(_internalDir, "a.json", "cached", DateTime.UtcNow);
        var file  = new SeedFile("/bundled/a.json", null, "https://example.com/a.json");
        var batch = Batch(SeedBatchOrigin.Bundled, file);
        var updater = CreateUpdater(NeverCallNetwork());

        var result = await updater.ResolveAsync([batch], allowNetwork: true, forceRefresh: false);

        Assert.AreEqual(SourceRefreshOutcome.UpToDate, result.Results.Single().Outcome);
        Assert.AreEqual(cachedPath, result.EffectiveBatches[0].Files[0].FilePath);
    }

    // Row 3: stale cached copy (past TTL) triggers a GET; success overwrites the cache and logs Information.
    [TestMethod]
    public async Task ResolveAsync_StaleCache_DownloadsAndOverwritesCache()
    {
        var cachedPath = WriteCachedFile(_internalDir, "a.json", "old content", DateTime.UtcNow.AddHours(-48));
        var file  = new SeedFile("/bundled/a.json", null, "https://example.com/a.json");
        var batch = Batch(SeedBatchOrigin.Bundled, file);
        var logger  = new RecordingLogger();
        var updater = CreateUpdater(RespondWith("new content"), logger);

        var result = await updater.ResolveAsync([batch], allowNetwork: true, forceRefresh: false);

        Assert.AreEqual(SourceRefreshOutcome.Updated, result.Results.Single().Outcome);
        Assert.AreEqual("new content", await File.ReadAllTextAsync(cachedPath));
        Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Information));
    }

    // Row 4: per-entry refreshIntervalHours overrides the global default.
    [TestMethod]
    public async Task ResolveAsync_PerEntryRefreshIntervalOverridesGlobalDefault()
    {
        // 2h old — fresh under the 24h global default, but stale under the 1h per-entry override.
        var cachedPath = WriteCachedFile(_internalDir, "a.json", "cached", DateTime.UtcNow.AddHours(-2));
        var file  = new SeedFile("/bundled/a.json", null, "https://example.com/a.json", RefreshIntervalHours: 1);
        var batch = Batch(SeedBatchOrigin.Bundled, file);
        var updater = CreateUpdater(RespondWith("refreshed"), defaultTtlHours: 24);

        var result = await updater.ResolveAsync([batch], allowNetwork: true, forceRefresh: false);

        Assert.AreEqual(SourceRefreshOutcome.Updated, result.Results.Single().Outcome);
        Assert.AreEqual("refreshed", await File.ReadAllTextAsync(cachedPath));
    }

    // Row 5: network failure logs a Warning and falls back to the most recent available copy; the operation still succeeds.
    [TestMethod]
    public async Task ResolveAsync_NetworkFailure_FallsBackToStaleCacheAndLogsWarning()
    {
        var cachedPath = WriteCachedFile(_internalDir, "a.json", "stale but usable", DateTime.UtcNow.AddHours(-48));
        var file  = new SeedFile("/bundled/a.json", null, "https://example.com/a.json");
        var batch = Batch(SeedBatchOrigin.Bundled, file);
        var logger  = new RecordingLogger();
        var updater = CreateUpdater(ThrowOnRequest(), logger);

        var result = await updater.ResolveAsync([batch], allowNetwork: true, forceRefresh: false);

        Assert.AreEqual(SourceRefreshOutcome.Failed, result.Results.Single().Outcome);
        Assert.AreEqual(cachedPath, result.EffectiveBatches[0].Files[0].FilePath);
        Assert.AreEqual("stale but usable", await File.ReadAllTextAsync(cachedPath));
        Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Warning));
    }

    // Row 6: forceSourceRefresh=true bypasses the TTL check.
    [TestMethod]
    public async Task ResolveAsync_ForceRefreshTrue_BypassesFreshTtlCheck()
    {
        var cachedPath = WriteCachedFile(_internalDir, "a.json", "old", DateTime.UtcNow); // fresh
        var file  = new SeedFile("/bundled/a.json", null, "https://example.com/a.json");
        var batch = Batch(SeedBatchOrigin.Bundled, file);
        var updater = CreateUpdater(RespondWith("forced update"));

        var result = await updater.ResolveAsync([batch], allowNetwork: true, forceRefresh: true);

        Assert.AreEqual(SourceRefreshOutcome.Updated, result.Results.Single().Outcome);
        Assert.AreEqual("forced update", await File.ReadAllTextAsync(cachedPath));
    }

    // Row 7: forceSourceRefresh=true does not bypass AutoUpdateSources=false, and logs a distinct message.
    [TestMethod]
    public async Task ResolveAsync_ForceRefreshTrueButAllowNetworkFalse_DoesNotBypassConfigAndLogsDistinctMessage()
    {
        var file  = new SeedFile("/bundled/a.json", null, "https://example.com/a.json");
        var batch = Batch(SeedBatchOrigin.Bundled, file);
        var logger  = new RecordingLogger();
        var updater = CreateUpdater(NeverCallNetwork(), logger);

        var result = await updater.ResolveAsync([batch], allowNetwork: false, forceRefresh: true);

        Assert.AreEqual(SourceRefreshOutcome.UpToDate, result.Results.Single().Outcome);
        Assert.IsTrue(logger.Entries.Any(e =>
            e.Message.Contains("forceSourceRefresh requested but") && e.Message.Contains("AutoUpdateSources")));
    }

    // Row 9: a user-imports manifest entry with a downloadUrl is downloaded and cached, same as a bundled entry.
    [TestMethod]
    public async Task ResolveAsync_UserImportsEntryWithDownloadUrl_DownloadedAndCachedSameAsBundled()
    {
        var file  = new SeedFile("/imports/b.json", null, "https://example.com/b.json");
        var batch = Batch(SeedBatchOrigin.UserImports, file);
        var updater = CreateUpdater(RespondWith("imported content"));

        var result = await updater.ResolveAsync([batch], allowNetwork: true, forceRefresh: false);

        var expectedPath = Path.Combine(_externalDir, "b.json");
        Assert.AreEqual(SourceRefreshOutcome.Updated, result.Results.Single().Outcome);
        Assert.AreEqual(expectedPath, result.EffectiveBatches[0].Files[0].FilePath);
        Assert.AreEqual("imported content", await File.ReadAllTextAsync(expectedPath));
    }

    // Row 10: bundled defaults to internal; user-imports defaults to external.
    [TestMethod]
    public async Task ResolveAsync_NoDownloadTargetOverride_DefaultsByOrigin()
    {
        var bundledFile = new SeedFile("/bundled/a.json", null, "https://example.com/a.json");
        var importsFile = new SeedFile("/imports/b.json", null, "https://example.com/b.json");
        var bundledBatch = Batch(SeedBatchOrigin.Bundled, bundledFile);
        var importsBatch = Batch(SeedBatchOrigin.UserImports, importsFile);
        var updater = CreateUpdater(RespondWith("x"));

        var result = await updater.ResolveAsync([bundledBatch, importsBatch], allowNetwork: true, forceRefresh: false);

        Assert.AreEqual(Path.Combine(_internalDir, "a.json"), result.EffectiveBatches[0].Files[0].FilePath);
        Assert.AreEqual(Path.Combine(_externalDir, "b.json"), result.EffectiveBatches[1].Files[0].FilePath);
    }

    // Row 11: an explicit per-entry downloadTarget routes that entry regardless of which manifest it came from.
    [TestMethod]
    public async Task ResolveAsync_ExplicitDownloadTargetOverride_RoutesRegardlessOfOrigin()
    {
        var file  = new SeedFile("/imports/c.json", null, "https://example.com/c.json", DownloadTarget: DownloadTarget.Internal);
        var batch = Batch(SeedBatchOrigin.UserImports, file);
        var updater = CreateUpdater(RespondWith("x"));

        var result = await updater.ResolveAsync([batch], allowNetwork: true, forceRefresh: false);

        Assert.AreEqual(Path.Combine(_internalDir, "c.json"), result.EffectiveBatches[0].Files[0].FilePath);
    }

    // Row 12: two entries whose resolved target paths collide are both skipped; a distinct Error names both sources
    // and the shared path; no silent overwrite occurs.
    [TestMethod]
    public async Task ResolveAsync_CollidingTargetPaths_SkipsBothAndLogsError()
    {
        var collidingPath = WriteCachedFile(_internalDir, "same.json", "pre-existing", DateTime.UtcNow);

        var fileA = new SeedFile("/bundled/same.json", null, "https://example.com/a.json");
        var fileB = new SeedFile("/bundled/other/same.json", null, "https://example.com/b.json");
        var batch = Batch(SeedBatchOrigin.Bundled, fileA, fileB);
        var logger  = new RecordingLogger();
        var updater = CreateUpdater(NeverCallNetwork(), logger);

        var result = await updater.ResolveAsync([batch], allowNetwork: true, forceRefresh: false);

        Assert.IsTrue(result.Results.All(r => r.Outcome == SourceRefreshOutcome.SkippedCollision));
        Assert.AreEqual("/bundled/same.json", result.EffectiveBatches[0].Files[0].FilePath);
        Assert.AreEqual("/bundled/other/same.json", result.EffectiveBatches[0].Files[1].FilePath);
        Assert.AreEqual("pre-existing", await File.ReadAllTextAsync(collidingPath));
        Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Error));
    }

    // -------------------------------------------------------------------------
    #region Helpers

    private SourceCacheUpdater CreateUpdater(IHttpClientFactory factory, RecordingLogger? logger = null, int defaultTtlHours = 24)
        => new(factory, new SourceCacheOptions(_internalDir, _externalDir, defaultTtlHours), logger ?? new RecordingLogger());

    private static SeedBatch Batch(SeedBatchOrigin origin, params SeedFile[] files)
        => new(files, ManifestPolicy.HardcodedDefault, origin.ToString(), origin);

    private static string WriteCachedFile(string dir, string fileName, string content, DateTime lastWriteTimeUtc)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        return path;
    }

    private static FakeHttpClientFactory NeverCallNetwork()
        => new(_ => throw new InvalidOperationException("network must not be called in this scenario"));

    private static FakeHttpClientFactory RespondWith(string body)
        => new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });

    private static FakeHttpClientFactory ThrowOnRequest()
        => new(_ => throw new HttpRequestException("simulated network failure"));

    private sealed class FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHttpMessageHandler(respond));
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private sealed class RecordingLogger : ILogger<SourceCacheUpdater>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    #endregion
}
