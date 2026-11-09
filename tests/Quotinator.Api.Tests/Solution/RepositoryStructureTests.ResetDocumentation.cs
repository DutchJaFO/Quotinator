using System.Text.RegularExpressions;

namespace Quotinator.Api.Tests.Solution;

/// <summary>
/// #482: no document contradicts another about what a Reset does, and <c>CLAUDE.md</c> points to the ADR
/// that defines a rule instead of restating it. Restated copies are how the contradictions this issue
/// corrects came about: <c>CLAUDE.md</c> and three ADRs went on describing a Reset that drops tables one by
/// one after it had stopped doing so, and ADR 015 described one that left the version history alone.
/// </summary>
public partial class RepositoryStructureTests
{
    /// <summary>
    /// Phrases that describe the Reset this issue replaces. Each was found in a current document on
    /// 2026-11-09; a later edit reintroducing one fails here rather than drifting unnoticed.
    /// </summary>
    private static readonly string[] SupersededResetPhrases =
    [
        "drops every table",
        "drop every table",
        "drops the entire database",
        "drops and rebuilds",
        "drops and recreates",
        "tables a Reset drops",
        "dropped table by table",
        "never wiped or replayed by a Reset",
        "DropAndRebuildAsync",
    ];

    /// <summary>
    /// Plan docs and released changelog entries record their own moment and are left as written; this
    /// file names the phrases, so it is excluded from its own search.
    /// </summary>
    private static bool IsHistoricalRecordOrThisGuard(string path)
    {
        string relative = Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');
        return relative.StartsWith("docs/milestones/", StringComparison.Ordinal)
            || relative.StartsWith("data/changelog/", StringComparison.Ordinal)
            || relative.EndsWith("CHANGELOG.md", StringComparison.Ordinal)
            || relative.Contains("/bin/", StringComparison.Ordinal)
            || relative.Contains("/obj/", StringComparison.Ordinal)
            || relative.EndsWith("RepositoryStructureTests.ResetDocumentation.cs", StringComparison.Ordinal);
    }

    [TestMethod]
    public void NothingDescribesAResetAsDroppingTables()
    {
        IEnumerable<string> searched =
            new[] { Path.Combine(RepoRoot, "CLAUDE.md") }
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot, "docs"), "*.md", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot, "addon"), "*.md", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot, "tests"), "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsHistoricalRecordOrThisGuard(path));

        List<string> found = [];
        foreach (string path in searched)
        {
            string text = File.ReadAllText(path);
            foreach (string phrase in SupersededResetPhrases.Where(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)))
                found.Add($"  {Path.GetRelativePath(RepoRoot, path).Replace('\\', '/')}: \"{phrase}\"");
        }

        Assert.IsEmpty(found, "A Reset is described as it no longer works:\n" + string.Join("\n", found));
    }

    /// <summary>
    /// Each <c>CLAUDE.md</c> passage about a Reset names the ADR that defines it and restates none of that
    /// ADR's rules. The marker terms are the rule's working parts, which only the ADR needs to name.
    /// </summary>
    [TestMethod]
    [DataRow("**No exception-based migration recovery.**", "027-", "preserveSchemaVersion", "GetAllTables", "ApplyMigrationPhaseAsync", "snapshotted")]
    [DataRow("**Audit-trail tables never purge dangling references", "014-", "GetAllTables", "DropAndRebuild")]
    public void ClaudeMdResetPassagesPointToTheirAdrs(string passageStart, string adrPrefix, params string[] restatedTerms)
    {
        string passage = ClaudeMdParagraphStartingWith(passageStart);

        Assert.Contains($"architecture-decisions/{adrPrefix}", passage, StringComparison.Ordinal, "the passage does not link its ADR");
        string[] restated = [.. restatedTerms.Where(t => passage.Contains(t, StringComparison.Ordinal))];
        Assert.IsEmpty(restated, "the passage restates its ADR: " + string.Join(", ", restated));
    }

    private const string PointerPrinciple = "points to the ADR that defines it";

    /// <summary>The principle itself is written where a later editor of either file will read it.</summary>
    [TestMethod]
    [DataRow("CLAUDE.md")]
    [DataRow("docs/architecture-decisions/README.md")]
    public void ThePointerPrincipleIsWrittenDown(string relativePath)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot, relativePath));

        Assert.IsTrue(text.Contains(PointerPrinciple, StringComparison.Ordinal), $"{relativePath} does not state that it {PointerPrinciple}");
    }

    /// <summary>
    /// Every ADR is in the index and in the solution, so one added for a rule <c>CLAUDE.md</c> points to
    /// is findable from both places a reader starts.
    /// </summary>
    [TestMethod]
    public void EveryAdrIsIndexed()
    {
        string folder = Path.Combine(RepoRoot, "docs", "architecture-decisions");
        string index = File.ReadAllText(Path.Combine(folder, "README.md"));
        string solution = File.ReadAllText(SlnxPath);

        string[] missing =
        [
            .. Directory.EnumerateFiles(folder, "*.md")
                .Select(Path.GetFileName)
                .Where(name => NumberedAdrName().IsMatch(name!))
                .Where(name => !index.Contains($"]({name})", StringComparison.Ordinal)
                            || !solution.Contains($"docs/architecture-decisions/{name}", StringComparison.Ordinal))
                .Select(name => name!),
        ];

        Assert.IsEmpty(missing, "ADRs missing from the index or the solution: " + string.Join(", ", missing));
    }

    [GeneratedRegex(@"^\d{3}-")]
    private static partial Regex NumberedAdrName();

    private static string ClaudeMdParagraphStartingWith(string start)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot, "CLAUDE.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        int at = text.IndexOf(start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, at, $"CLAUDE.md has no passage starting \"{start}\"");

        int end = text.IndexOf("\n\n**", at + start.Length, StringComparison.Ordinal);
        int heading = text.IndexOf("\n#", at + start.Length, StringComparison.Ordinal);
        int stop = new[] { end, heading, text.Length }.Where(i => i >= 0).Min();
        return text[at..stop];
    }
}
