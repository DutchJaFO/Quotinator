namespace Quotinator.Data.Schema;

/// <summary>One index on a table, as the integrity check compares it (#305).</summary>
/// <param name="Name">The index's name. Not compared for an automatic index, whose name is the engine's rather than the schema's.</param>
/// <param name="IsUnique">Whether the index is unique.</param>
/// <param name="Columns">The indexed columns, in order.</param>
/// <param name="Filter">The partial-index filter expression as written, or <see langword="null"/> when there is none.</param>
/// <param name="IsAutomatic">Whether the engine created the index to back a primary key or <c>UNIQUE</c> constraint.</param>
public sealed record SchemaIndex(string Name, bool IsUnique, IReadOnlyList<string> Columns, string? Filter, bool IsAutomatic);
