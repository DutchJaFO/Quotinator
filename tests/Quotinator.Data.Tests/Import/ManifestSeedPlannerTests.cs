using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class ManifestSeedPlannerTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void TestInitialize()
        => _tempDir = Directory.CreateTempSubdirectory("quotinator_manifestplanner_").FullName;

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Existing behavior (ported, regression-pinned) ───────────────────────────

    [TestMethod]
    public void PlanSeed_NoManifestAllowAutoCreateFalse_ReturnsAlphabeticalOrderNoFileWritten()
    {
        WriteFile("b.json", "[]");
        WriteFile("a.json", "[]");

        var planner = new ManifestSeedPlanner(NullLogger<ManifestSeedPlanner>.Instance);
        var (files, _) = planner.PlanSeed(_tempDir, ManifestPolicy.HardcodedDefault, allowAutoCreate: false);

        CollectionAssert.AreEqual(
            new[] { "a.json", "b.json" },
            files.Select(f => Path.GetFileName(f.FilePath)).ToList());
        Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "manifest.json")), "No manifest should be written when allowAutoCreate is false");
    }

    [TestMethod]
    public void PlanSeed_ManifestListsFiles_ReturnsListedFilesInDeclaredOrder()
    {
        WriteFile("z.json", "[]");
        WriteFile("a.json", "[]");
        WriteManifest(new JsonObject
        {
            ["files"] = new JsonArray(
                new JsonObject { ["file"] = "z.json", ["name"] = "z" },
                new JsonObject { ["file"] = "a.json", ["name"] = "a" })
        });

        var planner = new ManifestSeedPlanner(NullLogger<ManifestSeedPlanner>.Instance);
        var (files, _) = planner.PlanSeed(_tempDir, ManifestPolicy.HardcodedDefault, allowAutoCreate: false);

        CollectionAssert.AreEqual(
            new[] { "z.json", "a.json" },
            files.Select(f => Path.GetFileName(f.FilePath)).ToList());
    }

    [TestMethod]
    public void PlanSeed_UnlistedFilesPresent_AppendsThemAlphabeticallyAfterListed()
    {
        WriteFile("z.json", "[]");
        WriteFile("a.json", "[]");
        WriteFile("m.json", "[]");
        WriteManifest(new JsonObject
        {
            ["files"] = new JsonArray(new JsonObject { ["file"] = "z.json", ["name"] = "z" })
        });

        var planner = new ManifestSeedPlanner(NullLogger<ManifestSeedPlanner>.Instance);
        var (files, _) = planner.PlanSeed(_tempDir, ManifestPolicy.HardcodedDefault, allowAutoCreate: false);

        CollectionAssert.AreEqual(
            new[] { "z.json", "a.json", "m.json" },
            files.Select(f => Path.GetFileName(f.FilePath)).ToList());
    }

    [TestMethod]
    public void PlanSeed_AutoCreatedManifestPolicy_DefersToConfigPolicy()
    {
        WriteFile("a.json", "[]");
        var configPolicy = new ManifestPolicy(DuplicateResolutionPolicy.Overwrite);

        var planner = new ManifestSeedPlanner(NullLogger<ManifestSeedPlanner>.Instance);
        var (_, policy) = planner.PlanSeed(_tempDir, configPolicy, allowAutoCreate: true);

        Assert.AreEqual(configPolicy, policy, "Auto-created manifest omits duplicateResolution, so the resolved policy must equal the config-level policy");
    }

    [TestMethod]
    public void PlanSeed_InvalidManifestJson_FallsBackToAlphabeticalOrder()
    {
        WriteFile("b.json", "[]");
        WriteFile("a.json", "[]");
        File.WriteAllText(Path.Combine(_tempDir, "manifest.json"), "{ this is not valid json");

        var planner = new ManifestSeedPlanner(NullLogger<ManifestSeedPlanner>.Instance);
        var (files, policy) = planner.PlanSeed(_tempDir, ManifestPolicy.HardcodedDefault, allowAutoCreate: false);

        CollectionAssert.AreEqual(
            new[] { "a.json", "b.json" },
            files.Select(f => Path.GetFileName(f.FilePath)).ToList());
        Assert.AreEqual(ManifestPolicy.HardcodedDefault, policy);
    }

    [TestMethod]
    public void PlanSeed_EmptyDirectory_ReturnsEmptyListNoManifestWritten()
    {
        var planner = new ManifestSeedPlanner(NullLogger<ManifestSeedPlanner>.Instance);
        var (files, _) = planner.PlanSeed(_tempDir, ManifestPolicy.HardcodedDefault, allowAutoCreate: true);

        Assert.AreEqual(0, files.Count);
        Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "manifest.json")), "An empty directory must never get an auto-created manifest (files would violate minItems: 1)");
    }

    // ── Auto-create ───────────────────────────────────────────────────────────

    [TestMethod]
    public void PlanSeed_NoManifestAllowAutoCreateTrue_WritesManifestListingDiscoveredFilesAlphabetically()
    {
        WriteFile("b.json", "[]");
        WriteFile("a.json", "[]");

        var planner = new ManifestSeedPlanner(NullLogger<ManifestSeedPlanner>.Instance);
        planner.PlanSeed(_tempDir, ManifestPolicy.HardcodedDefault, allowAutoCreate: true);

        var manifestPath = Path.Combine(_tempDir, "manifest.json");
        Assert.IsTrue(File.Exists(manifestPath), "Manifest should be auto-created");

        var root  = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        var files = root["files"]!.AsArray().Select(e => e!["file"]!.GetValue<string>()).ToList();

        CollectionAssert.AreEqual(new[] { "a.json", "b.json" }, files);
        Assert.IsNull(root["duplicateResolution"], "Auto-created manifest must not freeze a duplicateResolution policy");
    }

    [TestMethod]
    public void PlanSeed_NoManifestAllowAutoCreateTrue_LogsWarning()
    {
        WriteFile("a.json", "[]");
        var logger  = new RecordingLogger<ManifestSeedPlanner>();
        var planner = new ManifestSeedPlanner(logger);

        planner.PlanSeed(_tempDir, ManifestPolicy.HardcodedDefault, allowAutoCreate: true);

        Assert.IsTrue(logger.Entries.Any(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("[Database - Init]") &&
            e.Message.Contains("auto-created")),
            "Expected a warning log line announcing the manifest auto-creation");
    }

    [TestMethod]
    public void PlanSeed_NoManifestAllowAutoCreateFalse_DoesNotLogWarning()
    {
        WriteFile("a.json", "[]");
        var logger  = new RecordingLogger<ManifestSeedPlanner>();
        var planner = new ManifestSeedPlanner(logger);

        planner.PlanSeed(_tempDir, ManifestPolicy.HardcodedDefault, allowAutoCreate: false);

        Assert.IsFalse(logger.Entries.Any(e => e.Level == LogLevel.Warning),
            "The disabled/bundled-dir path must stay at Information level — no warning");
    }

    // ── URL resolution — three source kinds ─────────────────────────────────────

    [TestMethod]
    public void PlanSeed_ManifestEntryHasDownloadUrl_ParsedIntoSeedFile()
    {
        WriteFile("a.json", "[]");
        WriteManifest(new JsonObject
        {
            ["files"] = new JsonArray(new JsonObject
            {
                ["file"]        = "a.json",
                ["name"]        = "a",
                ["url"]         = "https://example.com/a",
                ["downloadUrl"] = "https://example.com/raw/a.json"
            })
        });

        var planner = new ManifestSeedPlanner(NullLogger<ManifestSeedPlanner>.Instance);
        var (files, _) = planner.PlanSeed(_tempDir, ManifestPolicy.HardcodedDefault, allowAutoCreate: false);

        var seedFile = files.Single();
        Assert.AreEqual("https://example.com/a", seedFile.Url);
        Assert.AreEqual("https://example.com/raw/a.json", seedFile.DownloadUrl);
    }

    [TestMethod]
    public void PlanSeed_ManifestEntryHasGithubObject_ComputesUrlAndDownloadUrlFromConvention()
    {
        WriteFile("a.json", "[]");
        WriteManifest(new JsonObject
        {
            ["files"] = new JsonArray(new JsonObject
            {
                ["file"]   = "a.json",
                ["name"]   = "a",
                ["github"] = new JsonObject
                {
                    ["owner"]  = "someowner",
                    ["repo"]   = "somerepo",
                    ["path"]   = "data/a.json",
                    ["branch"] = "develop"
                }
            })
        });

        var planner = new ManifestSeedPlanner(NullLogger<ManifestSeedPlanner>.Instance);
        var (files, _) = planner.PlanSeed(_tempDir, ManifestPolicy.HardcodedDefault, allowAutoCreate: false);

        var seedFile = files.Single();
        Assert.AreEqual("https://github.com/someowner/somerepo", seedFile.Url);
        Assert.AreEqual("https://raw.githubusercontent.com/someowner/somerepo/develop/data/a.json", seedFile.DownloadUrl);
    }

    [TestMethod]
    public void PlanSeed_ManifestEntryHasGithubObjectNoBranch_DefaultsToMain()
    {
        WriteFile("a.json", "[]");
        WriteManifest(new JsonObject
        {
            ["files"] = new JsonArray(new JsonObject
            {
                ["file"]   = "a.json",
                ["name"]   = "a",
                ["github"] = new JsonObject
                {
                    ["owner"] = "someowner",
                    ["repo"]  = "somerepo",
                    ["path"]  = "a.json"
                }
            })
        });

        var planner = new ManifestSeedPlanner(NullLogger<ManifestSeedPlanner>.Instance);
        var (files, _) = planner.PlanSeed(_tempDir, ManifestPolicy.HardcodedDefault, allowAutoCreate: false);

        Assert.AreEqual("https://raw.githubusercontent.com/someowner/somerepo/main/a.json", files.Single().DownloadUrl);
    }

    [TestMethod]
    public void PlanSeed_ManifestEntryIsLocalOnly_ReturnsNullUrlAndDownloadUrl()
    {
        WriteFile("a.json", "[]");
        WriteManifest(new JsonObject
        {
            ["files"] = new JsonArray(new JsonObject { ["file"] = "a.json", ["name"] = "a" })
        });

        var planner = new ManifestSeedPlanner(NullLogger<ManifestSeedPlanner>.Instance);
        var (files, _) = planner.PlanSeed(_tempDir, ManifestPolicy.HardcodedDefault, allowAutoCreate: false);

        var seedFile = files.Single();
        Assert.IsNull(seedFile.Url);
        Assert.IsNull(seedFile.DownloadUrl);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void WriteFile(string name, string content)
        => File.WriteAllText(Path.Combine(_tempDir, name), content);

    private void WriteManifest(JsonObject manifest)
        => File.WriteAllText(Path.Combine(_tempDir, "manifest.json"), manifest.ToJsonString());

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
