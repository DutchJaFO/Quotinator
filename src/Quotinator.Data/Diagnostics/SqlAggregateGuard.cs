using System.Text.RegularExpressions;

namespace Quotinator.Data.Diagnostics;

/// <summary>
/// Heuristic guard against the SQL aggregate pattern described in CVE-2025-6965.
/// See <c>docs/sql-safety.md</c> for the design rationale, scope, and SQLite-specific
/// aggregate functions covered. See <c>docs/architecture-decisions/001-cve-2025-6965-sql-aggregate-guard.md</c>
/// for the Quotinator-level decision record.
/// </summary>
public static partial class SqlAggregateGuard
{
    // Covers standard SQL aggregates and SQLite-specific ones (GROUP_CONCAT, TOTAL).
    // T-SQL parser alternatives were evaluated and rejected — see docs/sql-safety.md.
    [GeneratedRegex(@"\b(COUNT|SUM|AVG|MIN|MAX|GROUP_CONCAT|TOTAL)\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex AggregatePattern();

    [GeneratedRegex(@"\b(GROUP\s+BY|HAVING)\b", RegexOptions.IgnoreCase)]
    private static partial Regex GroupByHavingPattern();

    /// <summary>
    /// Returns <c>true</c> when <paramref name="sql"/> contains both an aggregate function call
    /// and a <c>GROUP BY</c> or <c>HAVING</c> clause — the combination that triggers CVE-2025-6965.
    /// </summary>
    /// <remarks>
    /// A <c>true</c> result does not mean the query is incorrect; it means manual review is required
    /// to confirm that the number of aggregate terms does not exceed the number of output columns.
    /// See <c>docs/sql-safety.md</c> for the review process.
    /// </remarks>
    public static bool IsVulnerablePattern(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return false;
        return AggregatePattern().IsMatch(sql) && GroupByHavingPattern().IsMatch(sql);
    }
}
