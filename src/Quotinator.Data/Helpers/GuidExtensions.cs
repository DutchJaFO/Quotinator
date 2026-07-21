namespace Quotinator.Data.Helpers;

/// <summary>
/// The single real choke point for rendering a <see cref="Guid"/> as this project's canonical id
/// string (ADR 012) — lowercase <c>Guid.ToString("D")</c>. Every site that turns a <see cref="Guid"/>
/// into a string for storage, comparison, or API presentation must call <see cref="ToCanonicalId"/>
/// rather than repeating <c>.ToString("D")</c> inline.
/// </summary>
/// <remarks>
/// <see cref="GuidHandler"/> was originally documented as "the single global choke point" for this
/// casing, but that was wrong: Dapper's own built-in <c>typeMap</c> resolves a bare <see cref="Guid"/>
/// parameter's <c>DbType</c> before it ever consults a registered <c>ITypeHandler</c>, so
/// <see cref="GuidHandler.SetValue"/> was silently skipped for outbound parameters (it still ran for
/// reading a value back). Every call site in this codebase already worked around that, independently,
/// by pre-converting to a string before handing it to Dapper — which is exactly how a single casing
/// convention drifted into ~35 separately-typed-out <c>.ToString("D").ToUpperInvariant()</c> calls
/// instead of being enforced in one place. This extension method is the real fix: one method, one
/// format, referenced everywhere a <see cref="Guid"/> needs to become this project's canonical id
/// string. <see cref="DatabaseConfiguration.Configure"/> also now calls
/// <c>SqlMapper.RemoveTypeMap(typeof(Guid))</c> before registering <see cref="GuidHandler"/>, so a bare
/// <see cref="Guid"/>-typed Dapper parameter (should one slip through review without this extension) is
/// no longer a silent landmine either — see <see cref="GuidHandler"/>'s own remarks.
/// <para/>
/// <see cref="Quotinator.Data.Queries.IdClauses"/> wraps SQL comparisons in <c>LOWER(...)</c> — chosen
/// specifically (over <c>UPPER(...)</c>) to match this method's own lowercase output, so a value
/// produced here can always be bound directly into an <c>IN</c>-list compared with
/// <see cref="Quotinator.Data.Queries.IdClauses.In"/>/<c>NotIn</c> with no further transformation:
/// <c>LOWER(column) IN @ids</c> only needs the *column* side wrapped in SQL (SQLite has no syntax to
/// lowercase every element of a bound list), and since <see cref="ToCanonicalId"/>'s output already is
/// lowercase, the list values need no separate case-normalizing step. This was not always true — the
/// comparison mechanism briefly used <c>UPPER(...)</c> (matching an earlier uppercase-canonical
/// convention) and needed every bound list explicitly re-cased; see ADR 012's revision history for why
/// that mismatch is exactly the kind of drift this single-format choice is meant to prevent.
/// <para/>
/// <b>Never bind a raw <see cref="Guid"/> list directly as an <c>IN</c>-clause parameter</b> — call
/// <c>.Select(id =&gt; id.ToCanonicalId())</c> first, even though a *scalar* <see cref="Guid"/> parameter
/// correctly goes through <see cref="GuidHandler"/> (given the <c>RemoveTypeMap</c> fix above). Dapper's
/// list-parameter expansion does not reliably invoke a registered <c>ITypeHandler</c> per element the way
/// scalar parameter binding does — found live in <c>ConversationLineCountReader</c> and
/// <c>CharacterSourceLinkReader</c>'s own test suites (both passed a raw <c>IReadOnlyList&lt;Guid&gt;</c>
/// straight into an anonymous parameter object and silently matched zero rows) while switching
/// <see cref="Quotinator.Data.Queries.IdClauses"/> from <c>UPPER(...)</c> to <c>LOWER(...)</c>. Pre-canonicalizing
/// to strings sidesteps the ambiguity entirely — a string list binds each element as <c>DbType.String</c>
/// directly, no handler lookup involved.
/// </remarks>
public static class GuidExtensions
{
    /// <summary>Renders <paramref name="id"/> as this project's canonical lowercase id string.</summary>
    public static string ToCanonicalId(this Guid id) => id.ToString("D");
}
