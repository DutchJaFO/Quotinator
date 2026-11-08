using Quotinator.Constants.Routes;
using Quotinator.Data.Enums;
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
/// <para>
/// Extra objects are left out: they are reported by the integrity endpoint and the log, and never
/// degrade, so naming them here would suggest they are part of the fault.
/// </para>
/// </summary>
internal static class SchemaIntegrityGuidance
{
    /// <summary>The degraded-startup reason for a schema that does not match its migration path.</summary>
    /// <param name="result">The integrity check's result, holding at least one missing or different object.</param>
    internal static string Reason(SchemaIntegrityResult result)
    {
        IEnumerable<string> faults = result.Differences
            .Where(d => d.Kind != SchemaDifferenceKind.Extra)
            .Select(Describe);

        return "The database's schema does not match the one its recorded version defines: "
            + string.Join("; ", faults)
            + ". Quote content is not served from it. Resolve it with a database Reset "
            + $"(POST {ApiRoutes.DatabaseReset}), which rebuilds the schema and discards what the database "
            + "holds now, or restore a backup taken before the schema changed and restart. "
            + $"GET {ApiRoutes.DatabaseIntegrity} lists every difference.";
    }

    private static string Describe(SchemaDifference difference) => difference.Kind switch
    {
        SchemaDifferenceKind.Missing => $"missing {Noun(difference.ObjectKind)} {difference.ObjectName}",
        _ => $"{Noun(difference.ObjectKind)} {difference.ObjectName} has {Aspect(difference.Aspect)} "
            + $"'{difference.Actual}' where '{difference.Expected}' is expected",
    };

    private static string Noun(SchemaObjectKind kind) => kind switch
    {
        SchemaObjectKind.Table => "table",
        SchemaObjectKind.Column => "column",
        SchemaObjectKind.ForeignKey => "foreign key",
        SchemaObjectKind.Index => "index",
        SchemaObjectKind.View => "view",
        SchemaObjectKind.Trigger => "trigger",
        _ => kind.ToString(),
    };

    private static string Aspect(string? aspect) => aspect switch
    {
        nameof(SchemaColumn.DeclaredType) => "declared type",
        nameof(SchemaColumn.NotNull) => "not-null",
        nameof(SchemaColumn.DefaultValue) => "default value",
        nameof(SchemaColumn.PrimaryKeyPosition) => "primary-key position",
        nameof(SchemaForeignKey.OnDelete) => "on-delete action",
        nameof(SchemaForeignKey.OnUpdate) => "on-update action",
        nameof(SchemaIndex.IsUnique) => "uniqueness",
        nameof(SchemaIndex.Columns) => "columns",
        nameof(SchemaIndex.Filter) => "filter",
        nameof(SchemaObject.Table) => "table",
        _ => aspect ?? "a value",
    };
}
