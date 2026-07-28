using System.Text.RegularExpressions;

namespace Quotinator.Data.Diagnostics;

/// <summary>
/// Guard against an id column (primary key or foreign key, regardless of what C# type ultimately
/// receives it) reaching a caller unnormalized in its stored casing. Distinct from
/// <see cref="SqlIdCaseGuard"/>: that guard protects SQL <c>WHERE</c>/<c>JOIN</c> comparisons (a row
/// written under an old casing convention still <i>matches</i>); this guard protects the
/// <c>SELECT</c> column list itself (that same row still <i>renders</i> canonically once read back,
/// even when the query doing the reading applies no filter or join on that column at all).
/// </summary>
/// <remarks>
/// Deliberately matches every <c>*Id</c>-suffixed column in a <c>SELECT</c> list unconditionally, the
/// same way <see cref="SqlIdCaseGuard"/> matches every <c>*Id</c>-suffixed column in a
/// <c>WHERE</c>/<c>JOIN</c> unconditionally — no registry of "columns known to need it," and the same
/// strip-then-scan implementation technique <see cref="SqlIdCaseGuard.FindViolations"/> already uses
/// (remove every already-protected occurrence first, then flag whatever id-shaped reference remains),
/// rather than trying to encode "already protected" as a lookbehind — a lookbehind capable of correctly
/// skipping an arbitrary bracket-or-plain table-alias prefix inside <c>LOWER(...)</c> turned out
/// unreliable in practice (found live: <c>LOWER([w].[Id])</c> still slipped a bare "Id" match through).
/// <para/>
/// An earlier version of this guard scanned only a hand-maintained list of columns believed to be
/// <c>string</c>-typed on their C# side, on the reasoning that a <see cref="Guid"/>-typed property
/// renders lowercase for free via <c>System.Text.Json</c>'s own default formatting regardless of
/// stored casing. That reasoning is the same "safe because of how it's used today" assumption
/// <see cref="Queries.IdClauses.Join"/> already rejected for JOIN conditions (both sides of a join are
/// already canonical by construction, yet still wrapped, as defense-in-depth) — and it's demonstrably
/// fragile: a column's downstream C# type can change without the query ever being touched (exactly
/// what happened for masterdata reference fields — see <c>Quotinator.Core.Models.MasterDataReference</c>,
/// whose <c>Id</c> is <c>string</c>-typed specifically because a <see cref="Guid"/> wasn't enough for a
/// not-yet-existing row's id). Found live (#210) when the developer pointed directly at unwrapped
/// <c>Id</c>/<c>SeriesId</c>/<c>UniverseId</c> columns in <c>SqliteQuoteService.SelectBase</c> and asked
/// why this guard wasn't using the same technique as the JOIN guard. See ADR 012.
/// <para/>
/// <b>The one exemption</b>: <c>InitiatedById</c> (<c>SystemChangeLog</c>) is not always an id — it is
/// polymorphic (an import batch UUID, an HTTP route, or an enrichment provider name; see
/// <c>Entities.SystemChangeLog.InitiatedById</c>'s own doc comment) — forcing it lowercase would
/// corrupt legitimate mixed-case content in the non-id cases. This is the only column name excluded;
/// every other <c>*Id</c>-suffixed column, PK or FK, must be wrapped.
/// </remarks>
public static partial class SqlSelectPresentationGuard
{
    /// <summary>
    /// Column names excluded from this guard because they are not always an id despite the <c>Id</c>
    /// suffix — see this class's remarks for why <c>InitiatedById</c> is the sole entry.
    /// </summary>
    public static readonly IReadOnlyList<string> ExemptColumnNames = ["InitiatedById"];

    // Matches a SELECT ... FROM span (non-greedy, single query at a time — every query in this codebase
    // has exactly one top-level SELECT ... FROM per string). [\s\S] so a multi-line triple-quoted query
    // still matches.
    [GeneratedRegex(@"\bSELECT\b[\s\S]*?\bFROM\b", RegexOptions.IgnoreCase)]
    private static partial Regex SelectClausePattern();

    // An already-protected column: LOWER(...) wrapping a bare-or-alias-qualified, bracket-or-plain id
    // column, with its optional "AS alias" consumed too — stripped entirely before scanning, so nothing
    // inside it (the wrapped reference, or its alias if that alias also happens to end in "Id") can ever
    // register as a violation. Mirrors SqlIdCaseGuard's own ProtectedEqualityPattern/strip-then-scan
    // approach exactly, rather than trying to encode this as a lookbehind (see this class's remarks for
    // why the lookbehind approach was abandoned).
    [GeneratedRegex(@"LOWER\s*\(\s*(?:(?:\[\w+\]|\w+)\.)?(?:\[\w*Id\]|\w*Id)\s*\)(?:\s*AS\s+\w+)?", RegexOptions.IgnoreCase)]
    private static partial Regex ProtectedColumnPattern();

    // A bare or alias-qualified, optionally bracket-quoted column reference ending in "Id" — same shape
    // as SqlIdCaseGuard's own column-matching group. Run only after ProtectedColumnPattern has stripped
    // every already-wrapped occurrence, so the two remaining lookbehinds only need to handle what's left
    // unstripped: immediately preceded by "AS " (an alias name restating an *unwrapped* column, e.g.
    // "ser.Id AS SeriesId" — "SeriesId" here is a label, not a second raw column selection, so it must
    // not independently register as a violation regardless of what it's named); immediately preceded by
    // "@" (a bound-parameter placeholder — an INSERT-if-not-exists query's own projected-parameters-
    // then-WHERE-EXISTS idiom binds @Id/@QuoteId this way — a parameter reference is never a table
    // column, even though it lexically matches "ends in Id").
    [GeneratedRegex(@"(?<!AS\s+)(?<!@)(?<![A-Za-z0-9_])(?:(?:\[\w+\]|\w+)\.)?(?:\[(?<col>\w*Id)\]|(?<col>\w*Id))(?![A-Za-z0-9_])", RegexOptions.IgnoreCase)]
    private static partial Regex UnwrappedIdColumnPattern();

    /// <summary>
    /// Returns every unwrapped id column reference found in <paramref name="sql"/>'s
    /// <c>SELECT ... FROM</c> span — every match of <see cref="UnwrappedIdColumnPattern"/>, after
    /// <see cref="ProtectedColumnPattern"/> strips already-wrapped occurrences, whose captured column
    /// name isn't in <see cref="ExemptColumnNames"/>.
    /// </summary>
    public static IReadOnlyList<string> FindUnwrappedSelectColumns(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return [];

        var selectClauseMatch = SelectClausePattern().Match(sql);
        if (!selectClauseMatch.Success) return [];

        var scanned = ProtectedColumnPattern().Replace(selectClauseMatch.Value, " ");

        return UnwrappedIdColumnPattern().Matches(scanned)
            .Select(m => m.Groups["col"].Value)
            .Where(col => !ExemptColumnNames.Contains(col, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="sql"/>'s <c>SELECT</c> column list contains at least
    /// one unwrapped, non-exempt id column.
    /// </summary>
    public static bool IsMissingPresentationNormalization(string sql)
        => FindUnwrappedSelectColumns(sql).Count > 0;
}
