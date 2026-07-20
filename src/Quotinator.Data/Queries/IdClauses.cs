namespace Quotinator.Data.Queries;

/// <summary>
/// Builds the standard case-insensitive SQL fragment for comparing an id-named column (a primary key
/// or foreign key) against a bound parameter, a parameter list, or another id column — per ADR 012 and
/// CLAUDE.md's "GUID/enum/id comparisons are case-insensitive by default" rule.
/// </summary>
/// <remarks>
/// Every fixed query constant and dynamic factory method that compares an id column should build that
/// comparison through here rather than hand-typing <c>UPPER(...)</c> — a helper cannot forget the wrap
/// or apply it to only one side; a hand-typed comparison can, and repeatedly has (see ADR 012's
/// incident history). <see cref="Diagnostics.SqlIdCaseGuard"/> remains the backstop for any comparison
/// that doesn't go through this class — e.g. an id embedded in a larger hand-assembled fragment where
/// calling this helper isn't practical.
/// <para/>
/// Joins are wrapped the same as parameter comparisons, even though both sides of a join between two
/// id columns are already canonical by construction once write-side canonicalization
/// (<see cref="Helpers.EntityIdCanonicalizer"/>) is in place — wrapping here is deliberate
/// defense-in-depth, not a correction to a live bug, matching this project's standing rule to never
/// assume a comparison is safe today just because its only known callers currently behave.
/// </remarks>
public static class IdClauses
{
    /// <summary>
    /// <c>UPPER(column) = UPPER(@paramName)</c> — the standard case-insensitive WHERE-clause fragment
    /// for comparing an id column to a single bound parameter. <paramref name="paramName"/> is passed
    /// without its leading <c>@</c>.
    /// </summary>
    public static string Equals(string column, string paramName)
        => $"UPPER({column}) = UPPER(@{paramName})";

    /// <summary>
    /// <c>UPPER(column) IN @paramName</c> — the standard case-insensitive WHERE-clause fragment for
    /// comparing an id column against a bound list parameter. Only the column side can be wrapped in
    /// SQL; canonicalizing every id in the list before binding is the caller's responsibility (see
    /// <c>Sql.CharacterSources.SelectSourceReferencesForCharacters</c> for the precedent).
    /// <paramref name="paramName"/> is passed without its leading <c>@</c>.
    /// </summary>
    public static string In(string column, string paramName)
        => $"UPPER({column}) IN @{paramName}";

    /// <summary>
    /// <c>UPPER(column) NOT IN @paramName</c> — the exclusion counterpart to <see cref="In"/>, same
    /// column-side-only wrapping rationale. <paramref name="paramName"/> is passed without its
    /// leading <c>@</c>.
    /// </summary>
    public static string NotIn(string column, string paramName)
        => $"UPPER({column}) NOT IN @{paramName}";

    /// <summary>
    /// <c>UPPER(leftColumn) = UPPER(rightColumn)</c> — the standard case-insensitive JOIN-condition
    /// fragment between two id columns (typically a foreign key and the primary key it references).
    /// </summary>
    public static string Join(string leftColumn, string rightColumn)
        => $"UPPER({leftColumn}) = UPPER({rightColumn})";
}
