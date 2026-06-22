using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Json.Schema;

namespace Quotinator.Api.Tests.Solution;

/// <summary>Verifies that data/sources/ files are present on disk and registered in Quotinator.slnx.</summary>
/// <remarks>
/// A failing test here means a file was added or removed without updating the solution file,
/// or a file referenced in the solution no longer exists on disk.
/// </remarks>
[TestClass]
public class RepositoryStructureTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string SlnxPath = Path.Combine(RepoRoot, "Quotinator.slnx");
    private static readonly string DataSourcesDir = Path.Combine(RepoRoot, "data", "sources");

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Quotinator.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find repo root containing Quotinator.slnx.");
    }

    private static IReadOnlySet<string> LoadSlnxFilePaths()
    {
        var doc = XDocument.Load(SlnxPath);
        return doc.Descendants("File")
            .Select(e => e.Attribute("Path")?.Value)
            .Where(p => p is not null)
            .Select(p => p!.Replace('\\', '/'))
            .ToHashSet();
    }

    /// <summary>src/Quotinator.Api/resources/changelog.en.json must exist on disk as the English source file.</summary>
    [TestMethod]
    public void ChangelogEnJson_ExistsOnDisk()
    {
        Assert.IsTrue(
            File.Exists(Path.Combine(RepoRoot, "src", "Quotinator.Api", "resources", "changelog.en.json")),
            "src/Quotinator.Api/resources/changelog.en.json does not exist.");
    }

    /// <summary>CHANGELOG.md must exist on disk as a generated file.</summary>
    [TestMethod]
    public void ChangelogMd_ExistsOnDisk()
    {
        Assert.IsTrue(
            File.Exists(Path.Combine(RepoRoot, "CHANGELOG.md")),
            "CHANGELOG.md does not exist.");
    }

    /// <summary>addon/CHANGELOG.md must exist on disk as a generated file.</summary>
    [TestMethod]
    public void AddonChangelogMd_ExistsOnDisk()
    {
        Assert.IsTrue(
            File.Exists(Path.Combine(RepoRoot, "addon", "CHANGELOG.md")),
            "addon/CHANGELOG.md does not exist.");
    }

    /// <summary>data/quotes.json must not exist on disk — replaced by per-source files in data/sources/ (#61).</summary>
    [TestMethod]
    public void DataQuotesJson_DoesNotExistOnDisk()
    {
        var path = Path.Combine(RepoRoot, "data", "quotes.json");
        Assert.IsFalse(File.Exists(path),
            "data/quotes.json still exists on disk — it should have been deleted in #61.");
    }

    /// <summary>data/quotes.json must not be referenced in Quotinator.slnx.</summary>
    [TestMethod]
    public void DataQuotesJson_IsNotInSlnx()
    {
        var paths = LoadSlnxFilePaths();
        Assert.IsFalse(paths.Contains("data/quotes.json"),
            "data/quotes.json is still referenced in Quotinator.slnx.");
    }

    /// <summary>Every file listed in Quotinator.slnx under data/sources/ must exist on disk.</summary>
    [TestMethod]
    public void SlnxDataSourcesEntries_AllExistOnDisk()
    {
        var paths = LoadSlnxFilePaths();
        var sourceEntries = paths
            .Where(p => p.StartsWith("data/sources/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.IsNotEmpty(sourceEntries, "No data/sources/ entries found in Quotinator.slnx.");

        var failures = sourceEntries
            .Where(p => !File.Exists(Path.Combine(RepoRoot, p.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();

        Assert.IsEmpty(failures,
            $"Files listed in Quotinator.slnx do not exist on disk:\n{string.Join("\n", failures)}");
    }

    /// <summary>Every .json file in data/sources/ on disk must be listed in Quotinator.slnx.</summary>
    [TestMethod]
    public void DataSourcesFiles_OnDisk_AreAllInSlnx()
    {
        var paths = LoadSlnxFilePaths();
        var diskFiles = Directory.GetFiles(DataSourcesDir, "*.json")
            .Select(f => "data/sources/" + Path.GetFileName(f))
            .ToList();

        Assert.IsNotEmpty(diskFiles, "No .json files found in data/sources/.");

        var failures = diskFiles.Where(f => !paths.Contains(f)).ToList();

        Assert.IsEmpty(failures,
            $"Files exist in data/sources/ on disk but are missing from Quotinator.slnx:\n{string.Join("\n", failures)}");
    }

    /// <summary>
    /// Seed script with --no-fetch produces schema-valid files whose entry IDs exactly
    /// match the current baseline in data/sources/.
    /// </summary>
    [TestMethod]
    [TestCategory("Live")]
    public void SeedScript_WithNoFetch_ProducesFilesMatchingBaseline()
    {
        var schema = JsonSchema.FromText(
            File.ReadAllText(Path.Combine(RepoRoot, "schemas", "source-flat.schema.json")));

        var sources = JsonNode.Parse(
            File.ReadAllText(Path.Combine(RepoRoot, "scripts", "sources.json")))!.AsArray();

        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName               = "dotnet-script",
                Arguments              = $"scripts/seed.csx -- --no-fetch --output-dir \"{tempDir}\"",
                WorkingDirectory       = RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false
            })!;
            process.WaitForExit();

            Assert.AreEqual(0, process.ExitCode,
                $"Seed script failed:\n{process.StandardError.ReadToEnd()}");

            var failures = new List<string>();

            foreach (var src in sources)
            {
                var name     = src!["name"]!.GetValue<string>();
                var fileName = name.Replace("/", "_") + ".json";

                var outputPath   = Path.Combine(tempDir, fileName);
                var baselinePath = Path.Combine(RepoRoot, "data", "sources", fileName);

                if (!File.Exists(outputPath))
                {
                    failures.Add($"{fileName}: output file not found");
                    continue;
                }

                // Schema validation
                using var outputDoc = JsonDocument.Parse(File.ReadAllText(outputPath));
                var result = schema.Evaluate(outputDoc.RootElement,
                    new EvaluationOptions { OutputFormat = OutputFormat.List });

                if (!result.IsValid)
                {
                    var errors = (result.Details ?? [])
                        .Where(d => !d.IsValid && d.Errors is not null)
                        .SelectMany(d => d.Errors!.Select(e => $"  {d.InstanceLocation}: {e.Value}"));
                    failures.Add($"{fileName}: schema validation failed:\n{string.Join("\n", errors)}");
                }

                // ID set must exactly match baseline
                static HashSet<string> LoadIds(JsonElement root) =>
                    root.EnumerateArray()
                        .Select(e => e.GetProperty("id").GetString()!)
                        .ToHashSet();

                var outputIds   = LoadIds(outputDoc.RootElement);
                using var baselineDoc = JsonDocument.Parse(File.ReadAllText(baselinePath));
                var baselineIds = LoadIds(baselineDoc.RootElement);

                var missing = baselineIds.Except(outputIds).ToList();
                var extra   = outputIds.Except(baselineIds).ToList();

                if (missing.Count > 0)
                    failures.Add($"{fileName}: {missing.Count} IDs present in baseline are missing from output");
                if (extra.Count > 0)
                    failures.Add($"{fileName}: {extra.Count} IDs in output are not in baseline");
            }

            Assert.IsEmpty(failures,
                $"Seed script output does not match baseline:\n{string.Join("\n", failures)}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
