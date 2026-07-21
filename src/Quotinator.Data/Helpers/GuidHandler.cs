using System.Data;
using Dapper;

namespace Quotinator.Data.Helpers;

/// <summary>
/// Dapper TypeHandler for <see cref="Guid"/>.
/// Forces UUID values to TEXT storage in SQLite so they remain human-readable and comparable
/// across all query paths. Without this handler (and without <c>SqlMapper.RemoveTypeMap(typeof(Guid))</c>
/// — see remarks), Dapper sets <c>DbType.Guid</c> on the parameter; Microsoft.Data.Sqlite renders that
/// as an uppercase dashed-string, which does not necessarily match this project's canonical lowercase
/// id format.
/// </summary>
/// <remarks>
/// <b>This is not, by itself, a global choke point — it requires <c>SqlMapper.RemoveTypeMap(typeof(Guid))</c>
/// to run first.</b> Dapper's own built-in <c>typeMap</c> dictionary already has an entry for
/// <see cref="Guid"/> (→ <c>DbType.Guid</c>), and Dapper's parameter-binding code checks that
/// <c>typeMap</c> *before* it ever consults a registered <c>ITypeHandler</c> — so registering this
/// handler alone, without removing the built-in mapping first, silently does nothing for outbound
/// parameters (it still works for reading a value back via <see cref="Parse"/>, since result
/// deserialization doesn't consult <c>typeMap</c> the same way). <see cref="DatabaseConfiguration.Configure"/>
/// calls <c>SqlMapper.RemoveTypeMap(typeof(Guid))</c> immediately before registering this handler for
/// exactly this reason — remove that call and this class silently stops affecting parameter binding
/// again, with no compiler or test warning short of the casing itself drifting.
/// <para/>
/// Because this gap went unnoticed for a while, every call site in this codebase that needs a
/// <see cref="Guid"/> rendered as a string already does so explicitly via
/// <see cref="GuidExtensions.ToCanonicalId"/> rather than relying on Dapper to invoke this handler —
/// <see cref="GuidExtensions.ToCanonicalId"/> is this project's actual single choke point for the
/// canonical id string format (ADR 012). This handler remains registered (with the type-map removal)
/// as defence in depth, so a future bare <see cref="Guid"/>-typed Dapper parameter that skips
/// <c>ToCanonicalId()</c> still renders correctly instead of silently reintroducing mixed casing.
/// <para/>
/// The canonical format itself is lowercase, matching <see cref="Guid"/>'s own default <c>ToString()</c>
/// format and the conventional RFC 4122 UUID string representation most tooling expects — a readability
/// choice, independent of and unrelated to why SQL comparisons wrap both sides in <c>LOWER(...)</c>
/// (<see cref="Quotinator.Data.Diagnostics.SqlIdCaseGuard"/>, <see cref="Quotinator.Data.Queries.IdClauses"/>):
/// those exist purely so a comparison matches regardless of casing, and would work identically if the
/// canonical form were uppercase instead — <c>LOWER(...)</c> was chosen to match this project's actual
/// canonical form for convenience (a value from <see cref="GuidExtensions.ToCanonicalId"/> needs no
/// further transformation before binding into an <c>IN</c>-list), not because case-insensitivity itself
/// requires it.
/// </remarks>
public sealed class GuidHandler : SqlMapper.TypeHandler<Guid>
{
    /// <inheritdoc/>
    public override Guid Parse(object value)
        => Guid.Parse(value.ToString()!);

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, Guid value)
    {
        parameter.DbType = DbType.String;
        parameter.Value  = value.ToCanonicalId();
    }
}
