using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class SeedBatchesBuilderTests
{
    private string _bundledDir = null!;
    private string _importsDir = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        var root = Directory.CreateTempSubdirectory("quotinator_seedbatchesbuilder_").FullName;
        _bundledDir = Path.Combine(root, "bundled");
        _importsDir = Path.Combine(root, "imports");
        Directory.CreateDirectory(_bundledDir);
        Directory.CreateDirectory(_importsDir);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        var root = Path.GetDirectoryName(_bundledDir)!;
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private static IManifestSeedPlanner FakePlanner() => new StubManifestSeedPlanner();

    [TestMethod]
    public void Build_IncludeDefaultSourcesTrue_BundledBatchIncluded()
    {
        File.WriteAllText(Path.Combine(_bundledDir, "a.json"), "[]");

        var batches = SeedBatchesBuilder.Build(
            _bundledDir, _importsDir, ManifestPolicy.HardcodedDefault,
            includeDefaultSources: true, createMissingManifest: false,
            FakePlanner(), NullLogger.Instance);

        Assert.IsTrue(batches.Any(b => b.Label == "bundled sources"), "Bundled batch should be present when IncludeDefaultSources is true");
    }

    [TestMethod]
    public void Build_IncludeDefaultSourcesFalse_BundledBatchExcluded()
    {
        File.WriteAllText(Path.Combine(_bundledDir, "a.json"), "[]");

        var batches = SeedBatchesBuilder.Build(
            _bundledDir, _importsDir, ManifestPolicy.HardcodedDefault,
            includeDefaultSources: false, createMissingManifest: false,
            FakePlanner(), NullLogger.Instance);

        Assert.IsFalse(batches.Any(b => b.Label == "bundled sources"), "Bundled batch must be excluded when IncludeDefaultSources is false, even though files exist");
    }

    [TestMethod]
    public void Build_IncludeDefaultSourcesFalse_ImportsBatchStillIncluded()
    {
        File.WriteAllText(Path.Combine(_importsDir, "a.json"), "[]");

        var batches = SeedBatchesBuilder.Build(
            _bundledDir, _importsDir, ManifestPolicy.HardcodedDefault,
            includeDefaultSources: false, createMissingManifest: false,
            FakePlanner(), NullLogger.Instance);

        Assert.IsTrue(batches.Any(b => b.Label == "user imports"), "IncludeDefaultSources must only gate the bundled batch, not imports");
    }

    [TestMethod]
    public void Build_BundledDirMissing_LogsWarning()
    {
        var logger = new RecordingLogger<SeedBatchesBuilderTests>();
        var missingDir = Path.Combine(Path.GetDirectoryName(_bundledDir)!, "does-not-exist");

        SeedBatchesBuilder.Build(
            missingDir, _importsDir, ManifestPolicy.HardcodedDefault,
            includeDefaultSources: true, createMissingManifest: false,
            FakePlanner(), logger);

        Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("bundled sources directory not found")),
            "Expected a warning when the bundled directory doesn't exist");
    }

    [TestMethod]
    public void Build_ImportsDirExists_ImportsBatchIncluded()
    {
        File.WriteAllText(Path.Combine(_importsDir, "a.json"), "[]");

        var batches = SeedBatchesBuilder.Build(
            _bundledDir, _importsDir, ManifestPolicy.HardcodedDefault,
            includeDefaultSources: true, createMissingManifest: false,
            FakePlanner(), NullLogger.Instance);

        Assert.IsTrue(batches.Any(b => b.Label == "user imports"));
    }

    [TestMethod]
    public void Build_ImportsDirMissing_NoImportsBatch()
    {
        var missingDir = Path.Combine(Path.GetDirectoryName(_importsDir)!, "does-not-exist-imports");

        var batches = SeedBatchesBuilder.Build(
            _bundledDir, missingDir, ManifestPolicy.HardcodedDefault,
            includeDefaultSources: true, createMissingManifest: false,
            FakePlanner(), NullLogger.Instance);

        Assert.IsFalse(batches.Any(b => b.Label == "user imports"));
    }

    private sealed class StubManifestSeedPlanner : IManifestSeedPlanner
    {
        public (IReadOnlyList<SeedFile> Files, ManifestPolicy Policy) PlanSeed(string dir, ManifestPolicy configPolicy, bool allowAutoCreate)
        {
            var files = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.json").Select(f => new SeedFile(f, null)).ToList()
                : [];
            return (files, configPolicy);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
