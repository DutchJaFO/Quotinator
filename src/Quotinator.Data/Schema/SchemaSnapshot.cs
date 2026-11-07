namespace Quotinator.Data.Schema;

/// <summary>
/// A database's schema in every aspect the integrity check compares (#305). Engine-neutral by design
/// (ADR 003, design goal 5): it holds plain values, and only <see cref="ISchemaSnapshotReader"/>'s
/// implementation knows how an engine exposes them.
/// </summary>
/// <param name="Tables">The tables, each with its columns, foreign keys and indexes.</param>
/// <param name="Views">The views.</param>
/// <param name="Triggers">The triggers.</param>
public sealed record SchemaSnapshot(
    IReadOnlyList<SchemaTable> Tables,
    IReadOnlyList<SchemaObject> Views,
    IReadOnlyList<SchemaObject> Triggers)
{
    /// <summary>A schema with no objects at all.</summary>
    public static SchemaSnapshot Empty { get; } = new([], [], []);
}
