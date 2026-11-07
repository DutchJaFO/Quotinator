namespace Quotinator.Data.Schema;

/// <summary>One table and everything the integrity check compares on it (#305).</summary>
/// <param name="Name">The table's name, compared case-insensitively.</param>
/// <param name="Columns">The table's columns.</param>
/// <param name="ForeignKeys">The foreign keys declared on the table.</param>
/// <param name="Indexes">The indexes on the table.</param>
public sealed record SchemaTable(
    string Name,
    IReadOnlyList<SchemaColumn> Columns,
    IReadOnlyList<SchemaForeignKey> ForeignKeys,
    IReadOnlyList<SchemaIndex> Indexes);
