namespace Quotinator.Data.Schema;

/// <summary>A view or trigger, as the integrity check compares it (#305).</summary>
/// <param name="Name">The object's name, compared case-insensitively.</param>
/// <param name="Table">The table the object belongs to.</param>
public sealed record SchemaObject(string Name, string Table);
