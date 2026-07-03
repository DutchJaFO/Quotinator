namespace Quotinator.Data.Database;

/// <summary>
/// A consolidated DDL script that creates a consuming project's own domain schema directly at its
/// current version, used only when a database has zero pre-existing tables. Optional — a consumer
/// that supplies none always takes the full incremental migration path on a fresh database, unchanged.
/// </summary>
/// <remarks>
/// Covers only the consumer's own tables. <see cref="DatabaseInitializer"/> always creates its own
/// infrastructure tables (e.g. <c>System_AuditEntries</c>) directly, before this ever runs — a
/// consumer's baseline never needs to know about or recreate Quotinator.Data's own tables.
/// </remarks>
public sealed record SchemaBaseline
{
    /// <summary>DDL SQL that creates the consumer's own domain schema in one step, at its current (latest) version.</summary>
    public required string Sql { get; init; }
}
