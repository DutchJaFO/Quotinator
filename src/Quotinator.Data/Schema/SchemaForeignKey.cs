namespace Quotinator.Data.Schema;

/// <summary>One foreign key declared on a table, as the integrity check compares it (#305).</summary>
/// <param name="Columns">The owning table's columns, in order.</param>
/// <param name="ReferencedTable">The table the key references.</param>
/// <param name="ReferencedColumns">The referenced columns, in order.</param>
/// <param name="OnDelete">The action taken when a referenced row is deleted.</param>
/// <param name="OnUpdate">The action taken when a referenced key is updated.</param>
public sealed record SchemaForeignKey(
    IReadOnlyList<string> Columns,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns,
    string OnDelete,
    string OnUpdate);
