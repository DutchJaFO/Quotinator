using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Quotinator.Core.Database;
using Quotinator.Data.Database;
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

    /// <summary>
    /// Quotinator.Data must not reach Quotinator.Core, directly or transitively — ADR 004's
    /// domain-agnostic invariant, and the edge ADR 018 narrows to "only projects that are already
    /// domain-agnostic" (#304).
    /// </summary>
    /// <remarks>
    /// Nothing enforced this before #304, which is why it is easy to undo by accident: the moment Data
    /// needs something that happens to live in Core, adding the reference builds cleanly and the
    /// invariant is gone with no signal. #304's own INotificationTextSource exists precisely to avoid
    /// that reference, so the guard belongs alongside it. Walked transitively, since a reference added
    /// to Quotinator.Changelog or Quotinator.Logging would breach the invariant just as effectively.
    /// </remarks>
    [TestMethod]
    public void QuotinatorData_DoesNotReferenceQuotinatorCore()
    {
        string dataProject = Path.Combine(RepoRoot, "src", "Quotinator.Data", "Quotinator.Data.csproj");
        string coreProject = Path.Combine(RepoRoot, "src", "Quotinator.Core", "Quotinator.Core.csproj");
        Assert.IsTrue(File.Exists(dataProject), $"Quotinator.Data.csproj not found at {dataProject}.");
        Assert.IsTrue(File.Exists(coreProject), $"Quotinator.Core.csproj not found at {coreProject}.");

        // Positive controls first: "Data does not reach Core" and "this walk never matches anything"
        // produce the same result, so the run has to show the instrument finding something. The second
        // is reachable only by recursion — Core references Changelog through Data, never directly — so
        // together they prove both that a match is possible and that the transitive walk works.
        Assert.IsTrue(ReferencesProject(dataProject, "Quotinator.Changelog", []),
            "Positive control failed: Quotinator.Data references Quotinator.Changelog directly, so the "
            + "walk should find it. A failure here means the instrument is broken, not that the "
            + "invariant below holds.");
        Assert.IsTrue(ReferencesProject(coreProject, "Quotinator.Changelog", []),
            "Positive control failed: Quotinator.Core reaches Quotinator.Changelog only through "
            + "Quotinator.Data, so the walk should find it transitively. A failure here means the "
            + "recursion is broken and the invariant below is untested at depth.");

        List<string> chain = [];
        Assert.IsFalse(ReferencesProject(dataProject, "Quotinator.Core", chain),
            "Quotinator.Data reaches Quotinator.Core, which breaks its domain-agnostic invariant "
            + "(ADR 004) and ADR 018's dependency edge. A Core type Data needs is a signal to invert "
            + $"the dependency — declare the contract in Data and let Core implement it:\n  {string.Join("\n  → ", chain)}");
    }

    /// <summary>
    /// The walk above finds a forbidden reference when one exists, and reports none when it does not —
    /// proven against project files this test writes itself, since the real repository cannot supply the
    /// violating case (a Quotinator.Data → Quotinator.Core reference is circular and fails restore
    /// before any test runs).
    /// </summary>
    /// <remarks>
    /// Without this, <see cref="QuotinatorData_DoesNotReferenceQuotinatorCore"/> is an assertion that
    /// something is absent with nothing establishing the instrument could have found it — the shape
    /// `docs/automated-testing/README.md` calls out as passing just as confidently when the mechanism is
    /// broken. A walker that stopped recursing, or matched nothing at all, fails here and passes there.
    /// </remarks>
    [TestMethod]
    public void ProjectReferenceWalk_FindsAnIndirectReference_AndNotAnUnreferencedProject()
    {
        string fixtureDir = Directory.CreateTempSubdirectory("quotinator_projref_walk_").FullName;
        try
        {
            WriteProject(fixtureDir, "Alpha", "Beta");
            WriteProject(fixtureDir, "Beta", "Gamma");
            WriteProject(fixtureDir, "Gamma");
            WriteProject(fixtureDir, "Delta");

            string alpha = Path.Combine(fixtureDir, "Alpha.csproj");

            List<string> chain = [];
            Assert.IsTrue(ReferencesProject(alpha, "Gamma", chain),
                "The walk must follow Alpha → Beta → Gamma. A non-recursive walk fails here.");
            Assert.AreEqual("Alpha → Beta → Gamma", string.Join(" → ", chain),
                "The reported chain must name the actual path, so a real failure says how the reference is reached.");

            Assert.IsFalse(ReferencesProject(alpha, "Delta", []),
                "Delta is referenced by nothing. A walk that reports every project as reachable fails here.");
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }

    private static void WriteProject(string dir, string name, params string[] references)
    {
        string refs = string.Concat(references.Select(r => $"""    <ProjectReference Include="{r}.csproj" />{Environment.NewLine}"""));
        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
            {refs}  </ItemGroup>
            </Project>
            """);
    }

    private static bool ReferencesProject(string projectFile, string targetName, List<string> chain)
    {
        chain.Add(Path.GetFileNameWithoutExtension(projectFile));

        if (string.Equals(Path.GetFileNameWithoutExtension(projectFile), targetName, StringComparison.OrdinalIgnoreCase))
            return true;

        string projectDir = Path.GetDirectoryName(projectFile)!;
        XDocument doc = XDocument.Load(projectFile);

        foreach (XElement reference in doc.Descendants("ProjectReference"))
        {
            string? include = reference.Attribute("Include")?.Value;
            if (include is null)
                continue;

            string referenced = Path.GetFullPath(Path.Combine(projectDir, include.Replace('\\', Path.DirectorySeparatorChar)));
            if (File.Exists(referenced) && ReferencesProject(referenced, targetName, chain))
                return true;
        }

        chain.RemoveAt(chain.Count - 1);
        return false;
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
    /// Every runnable code block in the T2 suite — the index included — is PowerShell, and carries no
    /// construct that only a Unix shell provides.
    /// </summary>
    /// <remarks>
    /// PowerShell is this project's shell, and ADR 010 forbids Unix text-processing one-liners outright.
    /// The suite was nonetheless written in bash, which cost two false defect reports during #339's own
    /// full run: Git Bash's path conversion mounted a directory inside the Docker VM where
    /// <c>dotnet script</c> mounted the Windows one, and an unprotected <c>-e Quotinator__DataDir=/data</c>
    /// was rewritten to <c>C:/Program Files/Git/data</c>. Without a guard the next document written here
    /// is written in whatever its author last had in a terminal, which is how that state arose.
    ///
    /// Only fenced code is checked. Prose naming a construct is a record of a past defect — several such
    /// records are load-bearing in the index — and banning the word rather than the command would delete
    /// them. A fence containing <c>sh -c</c> is exempt for the same reason in reverse: what follows runs
    /// inside a Linux container, where a Unix shell is the only shell there is.
    /// </remarks>
    [TestMethod]
    public void EveryAutomatedTestingCodeBlock_IsPowerShell()
    {
        Assert.IsTrue(Directory.Exists(AutomatedTestingDir),
            $"{AutomatedTestingRelativePath} does not exist.");

        List<string> documents = [.. FindAutomatedTestingDocuments(), "README.md"];
        List<string> failures = [];

        foreach (string document in documents)
        {
            string[] lines = File.ReadAllLines(
                Path.Combine(AutomatedTestingDir, document.Replace('/', Path.DirectorySeparatorChar)));

            string? fenceLanguage = null;
            int fenceStart = 0;
            List<string> fenceLines = [];

            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("```", StringComparison.Ordinal))
                {
                    if (fenceLanguage is not null) fenceLines.Add(lines[i]);
                    continue;
                }

                if (fenceLanguage is null)
                {
                    fenceLanguage = lines[i][3..].Trim();
                    fenceStart = i + 1;
                    fenceLines = [];
                    continue;
                }

                if (ShellFenceLanguages.Contains(fenceLanguage))
                    failures.Add($"{document}:{fenceStart} — fence is ```{fenceLanguage}, not ```powershell");
                else if (fenceLanguage == "powershell")
                    failures.AddRange(UnixOnlyConstructs(document, fenceStart, fenceLines));

                fenceLanguage = null;
            }
        }

        Assert.IsEmpty(failures,
            "Every command in the T2 suite is PowerShell (ADR 010, and #339's own run):\n"
            + string.Join("\n", failures.Order()));
    }

    private static readonly HashSet<string> ShellFenceLanguages =
        new(StringComparer.OrdinalIgnoreCase) { "bash", "sh", "shell", "zsh" };

    /// <summary>
    /// Each entry pairs a construct no Windows PowerShell session provides with what to write instead,
    /// so a failure tells the reader the fix rather than only the fault.
    /// </summary>
    private static readonly (string Construct, string Instead)[] UnixOnlyShellConstructs =
    [
        ("MSYS_NO_PATHCONV", "nothing — it is a Git Bash workaround with nothing to work around here"),
        ("/dev/null",        "$null, or | Out-Null"),
        ("curl ",            "Invoke-RestMethod, or scripts/testing/http.csx"),
        ("curl.exe",         "Invoke-RestMethod, or scripts/testing/http.csx"),
        ("grep",             "Select-String — and to count, [regex]::Matches(...).Count"),
        ("cut -d",           "a property on the object ConvertFrom-Json returns"),
        ("wc -l",            "Measure-Object, or .Count on a parsed response"),
        ("; do",             "foreach (...) { ... }"),
        ("; done",           "foreach (...) { ... }"),
        ("<<'",              "a here-string: @'...'@"),
    ];

    /// <summary>
    /// Constructs recognised only where a command can begin, because their spelling also occurs inside
    /// ordinary text. Matching anywhere would flag <c># Wait until the app is healthy</c> as a shell
    /// loop and <c>isDismissed </c> as <c>sed</c> — both measured — and a guard that fires on prose gets
    /// worked around rather than obeyed.
    /// </summary>
    private static readonly (string Construct, string Instead)[] UnixOnlyShellCommands =
    [
        ("until", "http.csx --wait-for, or while (...) { Start-Sleep 1 }"),
        ("export", "$env:NAME = 'value'"),
        ("sed", "nothing — ADR 010 forbids it outright"),
        ("awk", "nothing — ADR 010 forbids it outright"),
    ];

    /// <summary>
    /// A fence carrying <c>sh -c</c> runs its payload inside a Linux container, so the Unix forms in it
    /// are correct rather than left over — the exemption is the whole fence because a continuation
    /// splits one such command across lines.
    /// </summary>
    private static IEnumerable<string> UnixOnlyConstructs(string document, int fenceStart, List<string> fenceLines)
    {
        if (fenceLines.Any(l => l.Contains("sh -c", StringComparison.Ordinal))) yield break;

        for (int i = 0; i < fenceLines.Count; i++)
        {
            foreach ((string construct, string instead) in UnixOnlyShellConstructs)
            {
                if (fenceLines[i].Contains(construct, StringComparison.Ordinal))
                    yield return $"{document}:{fenceStart + i} — `{construct.Trim()}` is not PowerShell; use {instead}";
            }

            string command = fenceLines[i].TrimStart();

            foreach ((string construct, string instead) in UnixOnlyShellCommands)
            {
                if (command.StartsWith($"{construct} ", StringComparison.Ordinal))
                    yield return $"{document}:{fenceStart + i} — `{construct}` is not PowerShell; use {instead}";
            }

            // `for` is the one shared spelling: PowerShell's own loop opens a parenthesis where bash's
            // names a variable, so only the bash form is a finding.
            if (command.StartsWith("for ", StringComparison.Ordinal) && !command.StartsWith("for (", StringComparison.Ordinal))
                yield return $"{document}:{fenceStart + i} — bash `for … in` is not PowerShell; use foreach (...) {{ ... }}";

            // Windows PowerShell 5.1 gives a single PSCustomObject no Count property, so a filter that
            // matches exactly one row prints nothing where the test expects 1 — correct for zero rows
            // and for two, blank for the one case a well-targeted assertion is most likely to produce.
            if (fenceLines[i].Contains("Where-Object", StringComparison.Ordinal)
                && fenceLines[i].Contains(".Count", StringComparison.Ordinal)
                && !fenceLines[i].Contains("@(", StringComparison.Ordinal))
            {
                yield return $"{document}:{fenceStart + i} — a filtered .Count needs @(…) around it, "
                    + "or it reports blank for exactly one match";
            }
        }
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

    /// <summary>
    /// A host port a document claims: either <c>--port N</c> passed to the environment script, or the
    /// host side of a raw <c>-p host:container</c> mapping for the few containers the script does not
    /// own.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"--port\s+(?<port>\d+)|-p\s+(?<port>\d+):\d+")]
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

    /// <summary>
    /// #302: neither initializer may make a DI-suppliable service dependency optional. A
    /// <c>IService? dep = null</c> parameter backed by <c>?? new Service()</c> turns "nobody registered
    /// this" into "here is a second, unmanaged instance" — silently, and only in whichever environment
    /// forgot to register it.
    /// <para>
    /// Found by the developer while reviewing this issue's own plan, which had proposed copying exactly
    /// that shape from <c>DatabaseInitializer</c>'s shipped <c>diskSpaceProvider</c>. A comment on the
    /// original explained why it was convenient; nothing stopped the next person reading it as a
    /// pattern, which is what this test is for.
    /// </para>
    /// <para>
    /// Deliberately scoped to constructors. Optional interface-typed <b>method</b> parameters
    /// (<c>IUnitOfWork? unitOfWork = null</c>, <c>IDbTransaction? transaction = null</c>) are ambient
    /// context a caller may legitimately omit, not injected dependencies — 78 of them exist and all are
    /// correct.
    /// </para>
    /// </summary>
    [TestMethod]
    public void InitializerConstructors_DoNotMakeAServiceDependencyOptional()
    {
        Type[] initializers = [typeof(DatabaseInitializer), typeof(QuotinatorDatabaseInitializer)];

        List<string> offenders = [];

        foreach (Type initializer in initializers)
        {
            foreach (ConstructorInfo constructor in initializer.GetConstructors())
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    if (!parameter.IsOptional) continue;
                    if (!parameter.ParameterType.IsInterface) continue;

                    offenders.Add($"{initializer.Name}.{parameter.Name} ({parameter.ParameterType.Name})");
                }
            }
        }

        Assert.IsEmpty(offenders,
            "These constructor parameters are interface-typed and optional, so a missing DI registration " +
            "produces a silent fallback instead of a startup failure:\n" + string.Join("\n", offenders));
    }

    public TestContext TestContext { get; set; }
}
