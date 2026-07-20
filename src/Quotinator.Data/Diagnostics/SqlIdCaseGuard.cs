using System.Text.RegularExpressions;

namespace Quotinator.Data.Diagnostics;

/// <summary>
/// Guard against a case-sensitive comparison between an id-named column and a caller-or-file-supplied
/// parameter, or between two id-named columns (a JOIN condition or correlated-subquery predicate).
/// See ADR 012 (<c>docs/architecture-decisions/012-canonicalize-entity-ids-at-capture.md</c>)
/// for why this matters: SQLite's default TEXT comparison is case-sensitive, so a column storing a
/// canonically-cased id must still be compared via <c>UPPER(...)</c> on both sides — relying on every
/// caller to already supply matching casing is exactly the assumption that caused the original
/// masterdata-404 finding (#207/#209/#210). The column-to-column case is wrapped as defense-in-depth
/// (#210) — both sides are already canonical by construction once write-side canonicalization is in
/// place, but the developer's explicit direction was to never assume a comparison stays safe just
/// because today's callers happen to agree.
/// </summary>
public static partial class SqlIdCaseGuard
{
    // A column reference ending in "Id" (optionally alias-qualified, optionally bracket-quoted)
    // immediately followed by "=", "IN", or "NOT IN" and a bound parameter. Deliberately does not
    // match a column-to-column JOIN condition (no "@" on the right) or a SET-clause assignment
    // target (handled separately, see StripUpdateSetClause).
    // The trailing "\)?" and the optional "UPPER(" before "@\w+" let this still match a *half*-wrapped
    // comparison (only the column or only the parameter side wrapped) — an unprotected match here is
    // exactly as unsafe as an unwrapped one, so it must still be flagged. NOT IN was found missing live
    // (#210's IdClauses refactor) — the operator alternation originally only covered "=" and "IN",
    // silently letting "q.Id NOT IN @excludedIds" (SqliteQuoteService.GetRandom) pass unflagged, since
    // "IN" alone doesn't match starting mid-way through "NOT IN".
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?:\w+\.)?\[?(\w*Id)\]?\)?\s*(=|NOT\s+IN|IN)\s*(?:UPPER\s*\(\s*)?@\w+", RegexOptions.IgnoreCase)]
    private static partial Regex IdComparisonPattern();

    // An already-protected equality: UPPER(column) = UPPER(@param). Both sides must be wrapped —
    // half-protected forms (only one side wrapped) are deliberately NOT matched here, so they still
    // fall through to IdComparisonPattern and get flagged.
    [GeneratedRegex(@"UPPER\s*\(\s*(?:\w+\.)?\[?\w*Id\]?\s*\)\s*=\s*UPPER\s*\(\s*@\w+\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex ProtectedEqualityPattern();

    // An already-protected IN clause: UPPER(column) IN @param. Only the column side can be wrapped in
    // SQL — protecting the list-parameter side is the caller's responsibility (canonicalize every id in
    // the list before binding), not expressible as a SQL-side wrap around an expanded IN list.
    [GeneratedRegex(@"UPPER\s*\(\s*(?:\w+\.)?\[?\w*Id\]?\s*\)\s*IN\s*@\w+", RegexOptions.IgnoreCase)]
    private static partial Regex ProtectedInClausePattern();

    // An already-protected NOT IN clause: UPPER(column) NOT IN @param — see ProtectedInClausePattern's
    // remark; same column-side-only wrapping rationale.
    [GeneratedRegex(@"UPPER\s*\(\s*(?:\w+\.)?\[?\w*Id\]?\s*\)\s*NOT\s+IN\s*@\w+", RegexOptions.IgnoreCase)]
    private static partial Regex ProtectedNotInClausePattern();

    // An unwrapped id-to-id comparison — a JOIN ON condition or correlated-subquery predicate between
    // two id columns, e.g. "s.Id = q.SourceId" or "cl2.ConversationId = cl.ConversationId". Both sides
    // require an alias prefix (this codebase never joins on a bare unqualified id column), which is
    // what distinguishes this from IdComparisonPattern's column-to-parameter case (no "@" involved
    // here at all). The optional leading "UPPER(" and trailing "\)?" on each side let this still match
    // a *half*-wrapped join (only one side UPPER()-wrapped) — a fully-wrapped join is stripped by
    // ProtectedJoinPattern before this ever runs (see FindViolations), so no double-counting occurs.
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?:UPPER\s*\(\s*)?\w+\.\[?(\w*Id)\]?\)?\s*=\s*(?:UPPER\s*\(\s*)?\w+\.\[?(\w*Id)\]?\)?(?![A-Za-z0-9_(])", RegexOptions.IgnoreCase)]
    private static partial Regex JoinComparisonPattern();

    // An already-protected join: UPPER(alias.ColumnId) = UPPER(alias2.ColumnId) — both sides wrapped,
    // neither side a bound parameter (that's ProtectedEqualityPattern's job).
    [GeneratedRegex(@"UPPER\s*\(\s*\w+\.\[?\w*Id\]?\s*\)\s*=\s*UPPER\s*\(\s*\w+\.\[?\w*Id\]?\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex ProtectedJoinPattern();

    // UPDATE ... SET <assignments> WHERE ... — the SET portion writes new values and is a capture-time
    // canonicalization concern (ADR 012, EntityIdCanonicalizer), not a read-side comparison concern this
    // guard covers. Stripped before scanning so a raw "SourceId = @sid" assignment isn't flagged.
    [GeneratedRegex(@"\bSET\b[\s\S]*?(?=\bWHERE\b)", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateSetClausePattern();

    // Detects a leading UPDATE keyword without spelling a bare quoted DML literal in this file — this
    // class lives outside Sql.cs/RepositorySql.cs, so SqlSourceScanTests.AllSqlStringLiterals_AreInCentralisedFiles
    // would otherwise flag a plain string constant of that keyword as an inline SQL literal.
    [GeneratedRegex(@"^\s*UPDATE\b", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingUpdateKeywordPattern();

    /// <summary>
    /// Returns <c>true</c> when <paramref name="sql"/> contains at least one case-sensitive comparison
    /// between an id-named column and a bound parameter, or between two id-named columns.
    /// </summary>
    public static bool IsCaseSensitiveIdComparison(string sql)
        => FindViolations(sql).Count > 0;

    /// <summary>Returns every case-sensitive id-comparison match found in <paramref name="sql"/>, for diagnostics.</summary>
    public static IReadOnlyList<string> FindViolations(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return [];

        var scanned = StripUpdateSetClause(sql);
        scanned = ProtectedEqualityPattern().Replace(scanned, " ");
        scanned = ProtectedNotInClausePattern().Replace(scanned, " ");
        scanned = ProtectedInClausePattern().Replace(scanned, " ");
        scanned = ProtectedJoinPattern().Replace(scanned, " ");

        var violations = IdComparisonPattern().Matches(scanned).Select(m => m.Value).ToList();
        violations.AddRange(JoinComparisonPattern().Matches(scanned).Select(m => m.Value));
        return violations;
    }

    private static string StripUpdateSetClause(string sql)
        => LeadingUpdateKeywordPattern().IsMatch(sql)
            ? UpdateSetClausePattern().Replace(sql, " ")
            : sql;
}
