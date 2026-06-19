using Quotinator.Data.Diagnostics;

namespace Quotinator.Core.Tests.Security;

[TestClass]
public class SqlSourceScanTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>
    /// Scans every C# source file under src/ and asserts that none contain the SQL aggregate
    /// pattern flagged by <see cref="SqlAggregateGuard.IsVulnerablePattern"/>.
    /// This is the automated gate for CVE-2025-6965 — see docs/sql-safety.md for the review
    /// process when a query must legitimately use GROUP BY with aggregate functions.
    /// </summary>
    [TestMethod]
    public void AllSqlInSourceFiles_NoVulnerableAggregatePatterns()
    {
        var srcDir = Path.Combine(RepoRoot, "src");
        Assert.IsTrue(Directory.Exists(srcDir), $"src/ directory not found at: {srcDir}");

        var violations = Directory
            .GetFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => SqlAggregateGuard.IsVulnerablePattern(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(RepoRoot, f))
            .ToList();

        Assert.AreEqual(0, violations.Count,
            $"Vulnerable SQL aggregate pattern found in:\n{string.Join("\n", violations)}\n\n" +
            "Review the query and consult docs/sql-safety.md before suppressing this failure.");
    }
}
