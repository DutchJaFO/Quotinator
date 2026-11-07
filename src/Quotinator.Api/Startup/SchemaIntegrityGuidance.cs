using Quotinator.Data.Schema;

namespace Quotinator.Api.Startup;

/// <summary>
/// What an operator is told when the database's schema does not match the one its migration path
/// defines (#305): every missing or different object, in engine-neutral words, and the two ways out.
/// <para>
/// Lives in the Api layer beside <see cref="SchemaOvershootGuidance"/> and for the same reason:
/// <c>Quotinator.Data</c> is domain-agnostic per ADR 004 and has no business deciding an operator's
/// wording. Allocates no <c>QTN-</c> code, since #333 owns code allocation.
/// </para>
/// </summary>
internal static class SchemaIntegrityGuidance
{
    /// <summary>The degraded-startup reason for a schema that does not match its migration path.</summary>
    /// <param name="result">The integrity check's result, holding at least one missing or different object.</param>
    internal static string Reason(SchemaIntegrityResult result)
    {
        _ = result;
        return string.Empty;
    }
}
