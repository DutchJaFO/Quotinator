using System.Text.Json;
using System.Xml.Linq;
using Json.Schema;
using Quotinator.Converters.BasicJsonArray;
using Quotinator.Converters.RegexArray;
using Quotinator.Core.Import;
using Quotinator.Data.Import;

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

    /// <summary>
    /// The only directories holding real projects. Enumerating these rather than walking the whole
    /// repository keeps checked-out git worktrees (e.g. .claude/worktrees/, git-excluded and holding
    /// stale copies of every .csproj) from producing phantom failures.
    /// </summary>
    private static readonly string[] ProjectRoots = ["src", "tests", "tools"];

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Quotinator.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find repo root containing Quotinator.slnx.");
    }

    private static HashSet<string> LoadSlnxFilePaths()
    {
        XDocument doc = XDocument.Load(SlnxPath);
        return [.. doc.Descendants("File")
            .Select(e => e.Attribute("Path")?.Value)
            .Where(p => p is not null)
            .Select(p => p!.Replace('\\', '/'))];
    }

    private static List<string> FindProjectFiles()
    {
        List<string> files = [];
        foreach (string root in ProjectRoots)
        {
            string dir = Path.Combine(RepoRoot, root);
            if (Directory.Exists(dir))
                files.AddRange(Directory.GetFiles(dir, "*.csproj", SearchOption.AllDirectories));
        }
        return files;
    }

    /// <summary>
    /// No PackageReference anywhere in the solution may carry an inline Version attribute — every
    /// package version is declared centrally in Directory.Packages.props (#320).
    /// </summary>
    /// <remarks>
    /// This is the regression guard for the failure mode #320 exists to remove: with per-project
    /// pins, a dependency bump that reaches some projects and not others resolves to two versions of
    /// the same package and fails restore with NU1605.
    /// </remarks>
    [TestMethod]
    public void PackageReferences_DoNotCarryInlineVersions()
    {
        List<string> projectFiles = FindProjectFiles();
        Assert.IsNotEmpty(projectFiles, "No .csproj files found under src/, tests/, or tools/.");

        List<string> failures = [];
        foreach (string file in projectFiles)
        {
            XDocument doc = XDocument.Load(file);
            foreach (XElement reference in doc.Descendants("PackageReference")
                         .Where(e => e.Attribute("Version") is not null))
            {
                string name = reference.Attribute("Include")?.Value ?? "(unnamed)";
                string version = reference.Attribute("Version")!.Value;
                failures.Add($"  {Path.GetRelativePath(RepoRoot, file).Replace('\\', '/')}: {name} = {version}");
            }
        }

        Assert.IsEmpty(failures,
            "PackageReference elements carry an inline Version attribute. Move the version to "
            + $"Directory.Packages.props as a <PackageVersion> entry:\n{string.Join("\n", failures)}");
    }

    /// <summary>Directory.Packages.props must exist at the repo root and switch central package management on.</summary>
    [TestMethod]
    public void DirectoryPackagesProps_ExistsAndEnablesCentralManagement()
    {
        string path = Path.Combine(RepoRoot, "Directory.Packages.props");
        Assert.IsTrue(File.Exists(path), "Directory.Packages.props does not exist at the repo root.");

        XDocument doc = XDocument.Load(path);
        string? enabled = doc.Descendants("ManagePackageVersionsCentrally").FirstOrDefault()?.Value;

        Assert.IsTrue(
            string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase),
            $"ManagePackageVersionsCentrally must be true in Directory.Packages.props (found: {enabled ?? "absent"}).");
    }

    /// <summary>Directory.Packages.props must be listed in Quotinator.slnx so it is visible in Visual Studio.</summary>
    [TestMethod]
    public void DirectoryPackagesProps_IsInSlnx()
    {
        HashSet<string> paths = LoadSlnxFilePaths();
        Assert.Contains("Directory.Packages.props", paths,
            "Directory.Packages.props is not referenced in Quotinator.slnx.");
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
        string path = Path.Combine(RepoRoot, "data", "quotes.json");
        Assert.IsFalse(File.Exists(path),
            "data/quotes.json still exists on disk — it should have been deleted in #61.");
    }

    /// <summary>data/quotes.json must not be referenced in Quotinator.slnx.</summary>
    [TestMethod]
    public void DataQuotesJson_IsNotInSlnx()
    {
        HashSet<string> paths = LoadSlnxFilePaths();
        Assert.DoesNotContain("data/quotes.json", paths,
            "data/quotes.json is still referenced in Quotinator.slnx.");
    }

    /// <summary>Every file listed in Quotinator.slnx under data/sources/ must exist on disk.</summary>
    [TestMethod]
    public void SlnxDataSourcesEntries_AllExistOnDisk()
    {
        HashSet<string> paths = LoadSlnxFilePaths();
        List<string> sourceEntries = [.. paths.Where(p => p.StartsWith("data/sources/", StringComparison.OrdinalIgnoreCase))];

        Assert.IsNotEmpty(sourceEntries, "No data/sources/ entries found in Quotinator.slnx.");

        List<string> failures = [.. sourceEntries.Where(p => !File.Exists(Path.Combine(RepoRoot, p.Replace('/', Path.DirectorySeparatorChar))))];

        Assert.IsEmpty(failures,
            $"Files listed in Quotinator.slnx do not exist on disk:\n{string.Join("\n", failures)}");
    }

    /// <summary>Every .json file in data/sources/ on disk must be listed in Quotinator.slnx.</summary>
    [TestMethod]
    public void DataSourcesFiles_OnDisk_AreAllInSlnx()
    {
        HashSet<string> paths = LoadSlnxFilePaths();
        List<string> diskFiles = [.. Directory.GetFiles(DataSourcesDir, "*.json").Select(f => "data/sources/" + Path.GetFileName(f))];

        Assert.IsNotEmpty(diskFiles, "No .json files found in data/sources/.");

        List<string> failures = [.. diskFiles.Where(f => !paths.Contains(f))];

        Assert.IsEmpty(failures,
            $"Files exist in data/sources/ on disk but are missing from Quotinator.slnx:\n{string.Join("\n", failures)}");
    }

    /// <summary>
    /// Each bundled source's converter plugin, run against a committed copy of its raw upstream
    /// format, produces schema-valid output whose entry IDs exactly match the current baseline in
    /// data/sources/. Replaces the historical seed.csx-based version of this test (removed alongside
    /// scripts/seed.csx/sources.json) — runs the converter in-process instead of shelling out to
    /// dotnet-script, so it no longer needs that tool installed to run in CI.
    /// </summary>
    [TestMethod]
    public async Task ConverterPlugins_AgainstRawFixtures_ProduceFilesMatchingBaseline()
    {
        JsonSchema schema = JsonSchema.FromText(
            File.ReadAllText(Path.Combine(RepoRoot, "schemas", "source-flat.schema.json")));

        JsonElement nikhilNamal17Options = JsonSerializer.SerializeToElement(new BasicJsonArrayConverterOptionsDto
        {
            PropertyMapping = new NamedFieldMapping { Source = "movie", Date = "year" }
        });

        JsonElement vilaboimOptions = JsonSerializer.SerializeToElement(new RegexArrayConverterOptionsDto
        {
            Pattern      = """^"(.+?)"\s+(.+)$""",
            GroupMapping = new IndexedFieldMapping { Quote = 1, Source = 2 }
        });

        (IQuoteSourceConverter Converter, string RawFixtureFile, string BaselineFile, JsonElement? Options)[] cases =
        [
            (new RegexArrayConverter(), "vilaboim_raw.json", "vilaboim_movie-quotes.json", vilaboimOptions),
            (new BasicJsonArrayConverter(), "nikhilnamal17_raw.json", "NikhilNamal17_popular-movie-quotes.json", nikhilNamal17Options),
        ];

        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            List<string> failures = [];

            foreach ((IQuoteSourceConverter? converter, string? rawFixtureFile, string? baselineFile, JsonElement? convOptions) in cases)
            {
                string rawPath      = Path.Combine(RepoRoot, "tests", "Quotinator.Api.Tests", "Solution", "Fixtures", rawFixtureFile);
                string outputPath   = Path.Combine(tempDir, baselineFile);
                string baselinePath = Path.Combine(RepoRoot, "data", "sources", baselineFile);

                await converter.ConvertAsync(rawPath, outputPath, convOptions, TestContext.CancellationToken);

                if (!File.Exists(outputPath))
                {
                    failures.Add($"{baselineFile}: output file not found");
                    continue;
                }

                // Schema validation
                using JsonDocument outputDoc = JsonDocument.Parse(File.ReadAllText(outputPath));
                EvaluationResults result = schema.Evaluate(outputDoc.RootElement,
                    new EvaluationOptions { OutputFormat = OutputFormat.List });

                if (!result.IsValid)
                {
                    IEnumerable<string> errors = (result.Details ?? [])
                        .Where(d => !d.IsValid && d.Errors is not null)
                        .SelectMany(d => d.Errors!.Select(e => $"  {d.InstanceLocation}: {e.Value}"));
                    failures.Add($"{baselineFile}: schema validation failed:\n{string.Join("\n", errors)}");
                }

                // ID set must exactly match baseline
                static HashSet<string> LoadIds(JsonElement root) =>
                    [.. root.EnumerateArray().Select(e => e.GetProperty("id").GetString()!)];

                HashSet<string> outputIds   = LoadIds(outputDoc.RootElement);
                using JsonDocument baselineDoc = JsonDocument.Parse(File.ReadAllText(baselinePath));
                HashSet<string> baselineIds = LoadIds(baselineDoc.RootElement);

                List<string> missing = [.. baselineIds.Except(outputIds)];
                List<string> extra   = [.. outputIds.Except(baselineIds)];

                if (missing.Count > 0)
                    failures.Add($"{baselineFile}: {missing.Count} IDs present in baseline are missing from output");
                if (extra.Count > 0)
                    failures.Add($"{baselineFile}: {extra.Count} IDs in output are not in baseline");
            }

            Assert.IsEmpty(failures,
                $"Converter plugin output does not match baseline:\n{string.Join("\n", failures)}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    public TestContext TestContext { get; set; }
}
