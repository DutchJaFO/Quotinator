namespace Quotinator.Data.Schema;

/// <summary>One column of a table, as the integrity check compares it (#305).</summary>
/// <param name="Name">The column's name, compared case-insensitively.</param>
/// <param name="DeclaredType">The column's declared type as written, compared as written.</param>
/// <param name="NotNull">Whether the column is declared <c>NOT NULL</c>.</param>
/// <param name="DefaultValue">The column's default expression as written, or <see langword="null"/> when it has none.</param>
/// <param name="PrimaryKeyPosition">The column's 1-based position in the primary key, or 0 when it is not part of it.</param>
public sealed record SchemaColumn(string Name, string DeclaredType, bool NotNull, string? DefaultValue, int PrimaryKeyPosition);
