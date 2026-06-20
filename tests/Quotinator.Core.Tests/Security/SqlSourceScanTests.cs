using System.Text.RegularExpressions;
using Quotinator.Data.Diagnostics;

namespace Quotinator.Core.Tests.Security;

[TestClass]
public class SqlSourceScanTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static IEnumerable<string> CSharpSourceFiles()
    {
        var srcDir = Path.Combine(RepoRoot, "src");
        Assert.IsTrue(Directory.Exists(srcDir), $"src/ directory not found at: {srcDir}");
        return Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories);
    }

    /// <summary>
    /// Scans every C# source file under src/ and asserts that none contain the SQL aggregate
    /// pattern flagged by <see cref="SqlAggregateGuard.IsVulnerablePattern"/>.
    /// This is the automated gate for CVE-2025-6965 — see docs/sql-safety.md for the review
    /// process when a query must legitimately use GROUP BY with aggregate functions.
    /// </summary>
    [TestMethod]
    public void AllSqlInSourceFiles_NoVulnerableAggregatePatterns()
    {
        var violations = CSharpSourceFiles()
            .Where(f => SqlAggregateGuard.IsVulnerablePattern(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(RepoRoot, f))
            .ToList();

        Assert.AreEqual(0, violations.Count,
            $"Vulnerable SQL aggregate pattern found in:\n{string.Join("\n", violations)}\n\n" +
            "Review the query and consult docs/sql-safety.md before suppressing this failure.");
    }

    /// <summary>
    /// Asserts that no C# source file outside the designated centralised SQL files contains
    /// DML string literals (SELECT / INSERT / UPDATE / DELETE).
    /// Enforces the string-centralisation policy documented in CLAUDE.md.
    /// </summary>
    [TestMethod]
    public void AllSqlStringLiterals_AreInCentralisedFiles()
    {
        // Files permitted to contain SQL DML string literals.
        // DatabaseInitializer.cs exception: migration constants are intentionally frozen there.
        var permittedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Sql.cs",
            "RepositorySql.cs",
            "DatabaseInitializer.cs",
        };

        // Matches a quote character immediately followed by a DML keyword.
        // Covers regular ("SELECT), verbatim (@"SELECT), interpolated ($"SELECT), and raw ("""SELECT) literals.
        var sqlDmlPattern = new Regex(@"""(SELECT|INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase);

        var violations = CSharpSourceFiles()
            .Where(f => !permittedFiles.Contains(Path.GetFileName(f)))
            .Where(f => sqlDmlPattern.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(RepoRoot, f))
            .ToList();

        Assert.AreEqual(0, violations.Count,
            $"SQL DML string literals found outside the centralised SQL files:\n{string.Join("\n", violations)}\n\n" +
            "Move the SQL to a constant or factory method in Sql.cs or RepositorySql.cs.");
    }
}
