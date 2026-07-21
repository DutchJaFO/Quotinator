namespace Quotinator.Data.Queries;

/// <summary>
/// Builds the standard case-insensitive SQL fragment for comparing an id-named column (a primary key
/// or foreign key) against a bound parameter, a parameter list, or another id column — per ADR 012 and
/// CLAUDE.md's "GUID/enum/id comparisons are case-insensitive by default" rule.
/// </summary>
/// <remarks>
/// Every fixed query constant and dynamic factory method that compares an id column should build that
/// comparison through here rather than hand-typing <c>LOWER(...)</c> — a helper cannot forget the wrap
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
/// <para/>
/// <b>Wraps in <c>LOWER(...)</c>, not <c>UPPER(...)</c></b> — deliberately matching
/// <see cref="Helpers.GuidExtensions.ToCanonicalId"/>'s own lowercase output (ADR 012), so a value
/// produced by that method can always be bound directly into an <see cref="In"/>/<see cref="NotIn"/>
/// list with no further transformation: <c>LOWER(column) IN @ids</c> can only wrap the *column* side in
/// SQL (SQLite has no syntax to lowercase every element of a bound list), so the list's own values must
/// already be lowercase — which they already are, coming straight out of
/// <see cref="Helpers.GuidExtensions.ToCanonicalId"/> or <see cref="Helpers.GuidHandler"/>. This was not
/// always the case: the wrapper briefly used <c>UPPER(...)</c> (matching an earlier uppercase-canonical
/// storage convention) and every bound list needed an explicit, easy-to-forget re-casing step before
/// binding — found live via <c>SqliteLinkRepository.QueryByIdsAsync</c> and the
/// <c>*ReferencesForManyAsync</c> readers silently returning zero rows during the system-wide lowercase
/// revision (ADR 012's revision history). Choosing the wrapper's casing to match the storage casing
/// eliminates that whole class of mismatch rather than requiring every list-binding call site to get a
/// second, easy-to-confuse casing step right.
/// </remarks>
public static class IdClauses
{
    /// <summary>
    /// <c>LOWER(column) = LOWER(@paramName)</c> — the standard case-insensitive WHERE-clause fragment
    /// for comparing an id column to a single bound parameter. <paramref name="paramName"/> is passed
    /// without its leading <c>@</c>.
    /// </summary>
    public static string Equals(string column, string paramName)
        => $"LOWER({column}) = LOWER(@{paramName})";

    /// <summary>
    /// <c>LOWER(column) IN @paramName</c> — the standard case-insensitive WHERE-clause fragment for
    /// comparing an id column against a bound list parameter. Only the column side can be wrapped in
    /// SQL; the bound list's own values must already be lowercase, which they are as long as they come
    /// from <see cref="Helpers.GuidExtensions.ToCanonicalId"/> (directly, or via <see cref="Helpers.GuidHandler"/>
    /// on a raw <see cref="Guid"/>-typed list parameter) — see this class's remarks.
    /// <paramref name="paramName"/> is passed without its leading <c>@</c>.
    /// </summary>
    public static string In(string column, string paramName)
        => $"LOWER({column}) IN @{paramName}";

    /// <summary>
    /// <c>LOWER(column) NOT IN @paramName</c> — the exclusion counterpart to <see cref="In"/>, same
    /// column-side-only wrapping rationale. <paramref name="paramName"/> is passed without its
    /// leading <c>@</c>.
    /// </summary>
    public static string NotIn(string column, string paramName)
        => $"LOWER({column}) NOT IN @{paramName}";

    /// <summary>
    /// <c>LOWER(leftColumn) = LOWER(rightColumn)</c> — the standard case-insensitive JOIN-condition
    /// fragment between two id columns (typically a foreign key and the primary key it references).
    /// </summary>
    public static string Join(string leftColumn, string rightColumn)
        => $"LOWER({leftColumn}) = LOWER({rightColumn})";

    /// <summary>
    /// <c>LOWER(column) AS alias</c> — the standard SELECT-list fragment for returning an id column
    /// (primary key or foreign key) to a caller. Applies uniformly, regardless of what C# type the
    /// caller ultimately deserializes the value into: a <see cref="Guid"/>-typed property happens to
    /// render lowercase anyway via <c>System.Text.Json</c>'s own default formatting, but this method
    /// does not rely on that — the same reasoning <see cref="Join"/> already established (wrap
    /// unconditionally, don't assume safety from how a value is used today) applies here too, and a
    /// downstream type can change from <see cref="Guid"/> to <c>string</c> without this query ever
    /// being touched (exactly what happened for masterdata reference fields — see
    /// <c>Quotinator.Core.Models.MasterDataReference</c>). <paramref name="alias"/> defaults to the
    /// bare column name (stripped of any table-alias prefix) when not supplied, so the wrapped
    /// expression's output name matches what an unwrapped bare reference would have produced.
    /// </summary>
    public static string SelectColumn(string column, string? alias = null)
    {
        alias ??= column.Contains('.') ? column[(column.LastIndexOf('.') + 1)..] : column;
        return $"LOWER({column}) AS {alias}";
    }
}
