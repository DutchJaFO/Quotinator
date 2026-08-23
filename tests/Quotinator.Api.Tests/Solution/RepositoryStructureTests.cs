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
public partial class RepositoryStructureTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string SlnxPath = Path.Combine(RepoRoot, "Quotinator.slnx");
    private static readonly string DataSourcesDir = Path.Combine(RepoRoot, "data", "sources");
    private static readonly string DataChangelogDir = Path.Combine(RepoRoot, "data", "changelog");

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

    /// <summary>
    /// Every Markdown file under docs/ must be listed in Quotinator.slnx, so it is visible in Visual
    /// Studio Solution Explorer — the place these documents are actually read.
    /// </summary>
    /// <remarks>
    /// ADR 018 shipped without its solution entry (commit 7d70708 added the file and its README index
    /// row but not the .slnx one) and stayed invisible until it was spotted by eye during #320.
    /// Nothing mechanical caught it, which is what this test exists to change.
    ///
    /// Enumerating from disk under docs/ rather than from git keeps this runnable without a git
    /// process; the .claude/worktrees/ copies that would otherwise interfere live outside docs/.
    /// </remarks>
    [TestMethod]
    public void DocsMarkdownFiles_OnDisk_AreAllInSlnx()
    {
        HashSet<string> paths = LoadSlnxFilePaths();
        string docsDir = Path.Combine(RepoRoot, "docs");

        List<string> diskFiles =
        [
            .. Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(RepoRoot, f).Replace('\\', '/'))
        ];

        Assert.IsNotEmpty(diskFiles, "No .md files found under docs/.");

        List<string> failures = [.. diskFiles.Where(f => !paths.Contains(f)).Order()];

        Assert.IsEmpty(failures,
            "Markdown files exist under docs/ but are missing from Quotinator.slnx, so they are "
            + $"invisible in Solution Explorer:\n{string.Join("\n", failures)}");
    }

    /// <summary>
    /// Every test document under docs/automated-testing/ must be reachable from the suite's own index,
    /// so a document cannot exist without appearing in the list a T2 pass works from.
    /// </summary>
    /// <remarks>
    /// The emptiness assertion is load-bearing, not defensive. "Every document is linked" is vacuously
    /// true over zero documents, so without it this test would pass green on a missing or empty folder —
    /// the exact state it exists to catch. See #339.
    /// </remarks>
    [TestMethod]
    public void EveryAutomatedTestingDocument_IsLinkedFromTheIndex()
    {
        Assert.IsTrue(Directory.Exists(AutomatedTestingDir),
            $"{AutomatedTestingRelativePath} does not exist.");

        string index = File.ReadAllText(Path.Combine(AutomatedTestingDir, "README.md"));
        List<string> documents = FindAutomatedTestingDocuments();

        Assert.IsNotEmpty(documents,
            $"No test documents found in {AutomatedTestingRelativePath} category folders.");

        List<string> failures = [.. documents.Where(d => !index.Contains(d, StringComparison.Ordinal)).Order()];

        Assert.IsEmpty(failures,
            "Test documents exist but are not linked from the suite index, so a T2 pass working from "
            + $"that index would silently skip them:\n{string.Join("\n", failures)}");
    }

    /// <summary>
    /// Every document the suite index links to must exist, so the list a T2 pass works from can never
    /// point at a test that was renamed or removed.
    /// </summary>
    [TestMethod]
    public void EveryAutomatedTestingIndexLink_ResolvesToAnExistingDocument()
    {
        Assert.IsTrue(Directory.Exists(AutomatedTestingDir),
            $"{AutomatedTestingRelativePath} does not exist.");

        string index = File.ReadAllText(Path.Combine(AutomatedTestingDir, "README.md"));

        List<string> linked =
        [
            .. MarkdownLinkTarget().Matches(index)
                .Select(m => m.Groups["target"].Value)
                .Where(t => t.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .Where(t => !t.StartsWith("..", StringComparison.Ordinal))
                .Distinct()
        ];

        List<string> failures =
        [
            .. linked.Where(t => !File.Exists(Path.Combine(AutomatedTestingDir, t.Replace('/', Path.DirectorySeparatorChar))))
                .Order()
        ];

        Assert.IsEmpty(failures,
            "The suite index links to documents that do not exist on disk:\n"
            + string.Join("\n", failures));
    }

    /// <summary>
    /// A link from one test document to another must resolve. The index guard above covers only links
    /// out of README.md, so without this the cross-references between documents can rot silently.
    /// </summary>
    [TestMethod]
    public void EveryAutomatedTestingCrossReference_ResolvesToAnExistingDocument()
    {
        Assert.IsTrue(Directory.Exists(AutomatedTestingDir),
            $"{AutomatedTestingRelativePath} does not exist.");

        List<string> failures = [];

        foreach (string document in Directory.GetFiles(AutomatedTestingDir, "*.md", SearchOption.AllDirectories))
        {
            string directory = Path.GetDirectoryName(document)!;

            foreach (System.Text.RegularExpressions.Match match in MarkdownLinkTarget().Matches(File.ReadAllText(document)))
            {
                string target = match.Groups["target"].Value;

                // Only relative links to another Markdown file are this test's concern — an anchor,
                // an absolute URL, or a link out to source/ADRs is somebody else's problem.
                if (!target.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                if (target.Contains("://", StringComparison.Ordinal)) continue;

                string resolved = Path.GetFullPath(
                    Path.Combine(directory, target.Replace('/', Path.DirectorySeparatorChar)));

                if (!File.Exists(resolved))
                    failures.Add($"{Path.GetRelativePath(RepoRoot, document).Replace('\\', '/')} → {target}");
            }
        }

        Assert.IsEmpty(failures,
            "Test documents link to files that do not exist:\n" + string.Join("\n", failures.Order()));
    }

    /// <summary>
    /// Every test document must name one of the environment profiles the suite index defines, so it
    /// establishes its own environment instead of inheriting whatever a previous test left running.
    /// </summary>
    /// <remarks>
    /// The profile list is read from the index rather than hardcoded here: a profile is a profile
    /// because it has its own section, so a document naming something the index never defined fails,
    /// and so does a document naming nothing at all. See #339, where splitting a single-file suite into
    /// one document per test silently dropped the Baseline section that had started the container for
    /// all of them — 21 of 43 documents were left driving a port nothing published.
    /// </remarks>
    [TestMethod]
    public void EveryAutomatedTestingDocument_NamesAKnownEnvironmentProfile()
    {
        Assert.IsTrue(Directory.Exists(AutomatedTestingDir),
            $"{AutomatedTestingRelativePath} does not exist.");

        string index = File.ReadAllText(Path.Combine(AutomatedTestingDir, "README.md"));

        List<string> profiles =
        [
            .. EnvironmentProfileHeading().Matches(index)
                .Select(m => m.Groups["name"].Value.Trim())
                .Distinct()
        ];

        Assert.IsNotEmpty(profiles,
            "The suite index defines no environment profiles, so no document can name one.");

        List<string> documents = FindAutomatedTestingDocuments();

        Assert.IsNotEmpty(documents,
            $"No test documents found in {AutomatedTestingRelativePath} category folders.");

        List<string> failures = [];

        foreach (string document in documents)
        {
            string path = Path.Combine(AutomatedTestingDir, document.Replace('/', Path.DirectorySeparatorChar));
            System.Text.RegularExpressions.Match declared =
                EnvironmentProfileDeclaration().Match(File.ReadAllText(path));

            if (!declared.Success)
            {
                failures.Add($"{document} — no **Environment:** field");
                continue;
            }

            // Constrained is a layer applied on top of a base, so a document may name more than one.
            foreach (string named in declared.Groups["name"].Value.Split('+', StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!profiles.Contains(named, StringComparer.OrdinalIgnoreCase))
                    failures.Add($"{document} — '{named}' is not a profile the index defines");
            }
        }

        Assert.IsEmpty(failures,
            "Test documents must name an environment profile the index defines, otherwise they inherit "
            + $"whatever a previous test left behind:\n{string.Join("\n", failures.Order())}\n\n"
            + $"Profiles defined by the index: {string.Join(", ", profiles)}");
    }

    /// <summary>
    /// The suite index's smoke-set table must name exactly the documents whose own Smoke field says
    /// yes, so the set a smoke pass runs cannot drift from what the documents claim.
    /// </summary>
    /// <remarks>
    /// The index previously listed the set by title only, which no guard could check — and it said so,
    /// carrying a note that the two could disagree. Linking each row makes the claim verifiable. See
    /// #339.
    /// </remarks>
    [TestMethod]
    public void SmokeSetInTheIndex_MatchesTheDocumentsMarkedSmoke()
    {
        Assert.IsTrue(Directory.Exists(AutomatedTestingDir),
            $"{AutomatedTestingRelativePath} does not exist.");

        string index = File.ReadAllText(Path.Combine(AutomatedTestingDir, "README.md"));

        System.Text.RegularExpressions.Match section = SmokeSetSection().Match(index);

        Assert.IsTrue(section.Success,
            "The suite index has no '## The designated smoke set' section.");

        List<string> listed =
        [
            .. MarkdownLinkTarget().Matches(section.Value)
                .Select(m => m.Groups["target"].Value)
                .Where(t => t.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .Order()
        ];

        List<string> marked =
        [
            .. FindAutomatedTestingDocuments()
                .Where(d => File.ReadAllText(
                        Path.Combine(AutomatedTestingDir, d.Replace('/', Path.DirectorySeparatorChar)))
                    .Contains("**Smoke:** yes", StringComparison.Ordinal))
                .Order()
        ];

        Assert.IsNotEmpty(marked, "No document is marked '**Smoke:** yes'.");

        Assert.AreSequenceEqual(marked, listed,
            "The index's smoke-set table and the documents' own Smoke fields disagree. The documents "
            + $"are authoritative.\nMarked in documents:\n{string.Join("\n", marked)}\n\n"
            + $"Linked from the index's smoke set:\n{string.Join("\n", listed)}");
    }

    /// <summary>
    /// Every test document states its steps as numbered subsections, each carrying its own expected
    /// result, so a failure is caught at the step that produced it rather than after the whole run.
    /// </summary>
    /// <remarks>
    /// Replaces a single trailing "Expected output" section, which reported a failure many commands too
    /// late and forced positional references — "the first call" — that a reader could only resolve by
    /// counting commands across several code blocks. See #339.
    /// </remarks>
    [TestMethod]
    public void EveryAutomatedTestingStep_CarriesItsOwnExpectedResult()
    {
        Assert.IsTrue(Directory.Exists(AutomatedTestingDir),
            $"{AutomatedTestingRelativePath} does not exist.");

        List<string> documents = FindAutomatedTestingDocuments();

        Assert.IsNotEmpty(documents,
            $"No test documents found in {AutomatedTestingRelativePath} category folders.");

        List<string> failures = [];

        foreach (string document in documents)
        {
            string text = File.ReadAllText(
                Path.Combine(AutomatedTestingDir, document.Replace('/', Path.DirectorySeparatorChar)));

            if (text.Contains("\n## Expected output", StringComparison.Ordinal))
            {
                failures.Add($"{document} — still has a trailing '## Expected output' section");
                continue;
            }

            List<System.Text.RegularExpressions.Match> steps =
                [.. NumberedStepHeading().Matches(text).Cast<System.Text.RegularExpressions.Match>()];

            if (steps.Count == 0)
            {
                failures.Add($"{document} — no numbered '### N. …' steps");
                continue;
            }

            // Checked per step rather than by counting: a document where one step carries three
            // expectations and another carries none would balance out under a total.
            for (int i = 0; i < steps.Count; i++)
            {
                int start = steps[i].Index;
                int end   = i + 1 < steps.Count ? steps[i + 1].Index : text.Length;

                if (!ExpectedResultLine().IsMatch(text[start..end]))
                    failures.Add($"{document} — step '{steps[i].Groups["title"].Value.Trim()}' has no expected result");

                // Numbers must read 1..N. Inserting a step and renumbering the rest by hand is exactly
                // where a duplicate or a gap creeps in, and a reader following the document cannot tell
                // which of two "step 3"s the next instruction meant.
                string expected = (i + 1).ToString();
                string actual   = steps[i].Groups["number"].Value;

                if (actual != expected)
                    failures.Add(
                        $"{document} — step '{steps[i].Groups["title"].Value.Trim()}' is numbered "
                        + $"{actual} where {expected} was expected");
            }
        }

        Assert.IsEmpty(failures,
            "Test documents must state their steps as numbered subsections, each with its own expected "
            + $"result:\n{string.Join("\n", failures.Order())}");
    }

    /// <summary>
    /// Every test document publishes the ports it talks to, and no two documents publish the same one,
    /// so any pair of tests can run at the same time without reaching each other's state.
    /// </summary>
    /// <remarks>
    /// A suite sharing one container is sequential by construction and needs a restore step between
    /// every pair to stay honest — coupling wearing a cleanup label. Per-test containers remove it, and
    /// a colliding host port would quietly put it back. See #339.
    /// </remarks>
    [TestMethod]
    public void EveryAutomatedTestingDocument_PublishesThePortsItUses_AndSharesNoneWithAnother()
    {
        Assert.IsTrue(Directory.Exists(AutomatedTestingDir),
            $"{AutomatedTestingRelativePath} does not exist.");

        List<string> documents = FindAutomatedTestingDocuments();

        Assert.IsNotEmpty(documents,
            $"No test documents found in {AutomatedTestingRelativePath} category folders.");

        Dictionary<string, string> publishedBy = [];
        List<string> failures = [];

        foreach (string document in documents)
        {
            string text = File.ReadAllText(
                Path.Combine(AutomatedTestingDir, document.Replace('/', Path.DirectorySeparatorChar)));

            HashSet<string> published =
            [
                .. PublishedHostPort().Matches(text).Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups["port"].Value)
            ];

            HashSet<string> used =
            [
                .. LocalhostPort().Matches(text).Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups["port"].Value)
            ];

            foreach (string port in used.Except(published).Order())
                failures.Add($"{document} — talks to localhost:{port} but never publishes it");

            foreach (string port in published.Order())
            {
                // A scheme that derives a port by appending digits runs off the end of the port range
                // without anything complaining: `docker run -p 181031:8080` just fails at runtime, and
                // a uniqueness check alone is perfectly happy with it.
                if (!int.TryParse(port, out int number) || number is < 1 or > 65535)
                    failures.Add($"{document} — {port} is not a TCP port (the maximum is 65535)");

                if (publishedBy.TryGetValue(port, out string? owner))
                    failures.Add($"{document} — publishes {port}, already published by {owner}");
                else
                    publishedBy[port] = document;
            }
        }

        Assert.IsEmpty(failures,
            "Every test owns its own container and port, so any two can run concurrently:\n"
            + string.Join("\n", failures.Order()));
    }

    private const string AutomatedTestingRelativePath = "docs/automated-testing";

    private static readonly string AutomatedTestingDir =
        Path.Combine(RepoRoot, "docs", "automated-testing");

    /// <summary>
    /// Test documents live in category subfolders; the index itself sits at the folder root and is not
    /// one of them.
    /// </summary>
    private static List<string> FindAutomatedTestingDocuments() =>
    [
        .. Directory.GetDirectories(AutomatedTestingDir)
            .SelectMany(d => Directory.GetFiles(d, "*.md", SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(AutomatedTestingDir, f).Replace('\\', '/'))
    ];

    [System.Text.RegularExpressions.GeneratedRegex(@"\]\((?<target>[^)]+)\)")]
    private static partial System.Text.RegularExpressions.Regex MarkdownLinkTarget();

    /// <summary>
    /// A profile is defined by having its own section in the index, so the guard cannot drift from the
    /// documentation the way a hardcoded list would.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(
        @"^####\s+(?<name>.+?)\s*$",
        System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex EnvironmentProfileHeading();

    [System.Text.RegularExpressions.GeneratedRegex(
        @"^\*\*Environment:\*\*\s*(?<name>.+?)\s*$",
        System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex EnvironmentProfileDeclaration();

    [System.Text.RegularExpressions.GeneratedRegex(
        @"^###\s+(?<number>\d+)\.\s+(?<title>.+?)\s*$",
        System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex NumberedStepHeading();

    /// <summary>
    /// Matches the plain <c>**Expected:**</c> and the qualified <c>**Expected — …:**</c> form, which a
    /// step uses when it carries more than one expectation or when the expectation needs labelling —
    /// for instance as an original that is known to be unreachable.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(
        @"^\*\*Expected\b",
        System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex ExpectedResultLine();

    /// <summary>The host side of a <c>-p host:container</c> port mapping.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"-p\s+(?<port>\d+):\d+")]
    private static partial System.Text.RegularExpressions.Regex PublishedHostPort();

    /// <summary>A port a document actually sends requests to.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"localhost:(?<port>\d+)")]
    private static partial System.Text.RegularExpressions.Regex LocalhostPort();

    /// <summary>
    /// The smoke-set section, from its own heading up to the next top-level heading.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(
        @"^## The designated smoke set$.*?(?=^## )",
        System.Text.RegularExpressions.RegexOptions.Multiline
        | System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex SmokeSetSection();

    /// <summary>data/changelog/changelog.en.json must exist on disk as the English source file.</summary>
    [TestMethod]
    public void ChangelogEnJson_ExistsOnDisk()
    {
        Assert.IsTrue(
            File.Exists(Path.Combine(DataChangelogDir, "changelog.en.json")),
            "data/changelog/changelog.en.json does not exist.");
    }

    /// <summary>Every data/changelog/ entry listed in Quotinator.slnx must exist on disk.</summary>
    [TestMethod]
    public void SlnxDataChangelogEntries_AllExistOnDisk()
    {
        HashSet<string> paths = LoadSlnxFilePaths();
        List<string> changelogEntries = [.. paths.Where(p => p.StartsWith("data/changelog/", StringComparison.OrdinalIgnoreCase))];

        Assert.IsNotEmpty(changelogEntries, "No data/changelog/ entries found in Quotinator.slnx.");

        List<string> failures = [.. changelogEntries.Where(p => !File.Exists(Path.Combine(RepoRoot, p.Replace('/', Path.DirectorySeparatorChar))))];

        Assert.IsEmpty(failures,
            $"Files listed in Quotinator.slnx do not exist on disk:\n{string.Join("\n", failures)}");
    }

    /// <summary>Every .json file in data/changelog/ on disk must be listed in Quotinator.slnx.</summary>
    [TestMethod]
    public void DataChangelogFiles_OnDisk_AreAllInSlnx()
    {
        HashSet<string> paths = LoadSlnxFilePaths();
        List<string> diskFiles = [.. Directory.GetFiles(DataChangelogDir, "*.json").Select(f => "data/changelog/" + Path.GetFileName(f))];

        Assert.IsNotEmpty(diskFiles, "No .json files found in data/changelog/.");

        List<string> failures = [.. diskFiles.Where(f => !paths.Contains(f))];

        Assert.IsEmpty(failures,
            $"Files exist in data/changelog/ on disk but are missing from Quotinator.slnx:\n{string.Join("\n", failures)}");
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
