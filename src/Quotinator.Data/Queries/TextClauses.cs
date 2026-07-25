namespace Quotinator.Data.Queries;

/// <summary>
/// Builds the standard case-insensitive SQL fragment for comparing a non-id text column — a
/// Name/Title natural key, a Status/EntityType/TableName discriminator, or a Language code — against
/// a bound parameter. Sibling to <see cref="IdClauses"/>, deliberately kept separate rather than
/// folded into it: <see cref="IdClauses"/> is scoped to what its name and ADR 012 actually govern
/// (id columns specifically); this class covers everything CLAUDE.md's "case-insensitive by default"
/// rule extends to beyond ids (#211).
/// </summary>
/// <remarks>
/// Only one method is provided — unlike <see cref="IdClauses"/>, this column class needs no
/// <c>Join</c>/<c>In</c>/<c>SelectColumn</c> counterpart. An id column can arrive with inconsistent
/// casing at the <em>write</em> side (a file-authored explicit id is under no obligation to match a
/// prior write's casing), which is why <see cref="IdClauses.SelectColumn"/> exists to normalize
/// presentation on read regardless of what's stored. The columns this class covers don't have that
/// problem: their write side is always internally generated with already-consistent casing (a fixed
/// C# string literal, or an enum's own <c>.ToString()</c>) — only the <em>comparison</em> side ever
/// needs to tolerate an externally-supplied differently-cased filter value (a query parameter or an
/// import file's own field), so wrapping the equality comparison alone is sufficient.
/// <para/>
/// Wraps in <c>LOWER(...)</c>, matching this project's current canonical direction (ADR 012's
/// revision history) — found via #211 that some of these columns had drifted onto <c>UPPER(...)</c>
/// instead, left over from before that convention was settled; migrating every call site through this
/// helper is what makes a future direction change touch one place instead of requiring a fresh audit.
/// <see cref="Diagnostics.SqlTextCaseGuard"/> is the backstop for any comparison that doesn't go
/// through this class, mirroring <see cref="Diagnostics.SqlIdCaseGuard"/>'s role for
/// <see cref="IdClauses"/>.
/// </remarks>
public static class TextClauses
{
    /// <summary>
    /// <c>LOWER(column) = LOWER(@paramName)</c> — the standard case-insensitive WHERE-clause fragment
    /// for comparing a non-id text column to a single bound parameter. Not for id columns — use
    /// <see cref="IdClauses.Equals"/> for those. <paramref name="paramName"/> is passed without its
    /// leading <c>@</c>.
    /// </summary>
    public static string Equals(string column, string paramName)
        => $"LOWER({column}) = LOWER(@{paramName})";
}
