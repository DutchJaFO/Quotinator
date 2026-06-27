namespace Quotinator.Data.Database;

/// <summary>A numbered, append-only DDL script that advances the database schema by one version.</summary>
/// <remarks>
/// Migrations are applied in <see cref="Version"/> order. Once a migration has been applied to any
/// real database it is frozen — never reorder, edit, or remove existing entries.
/// Every SQL statement must be idempotent (use <c>IF NOT EXISTS</c> / <c>IF EXISTS</c>).
/// </remarks>
public sealed record SchemaMigration
{
    /// <summary>1-based version number. Must be sequential with no gaps.</summary>
    public required int Version { get; init; }

    /// <summary>DDL SQL to execute atomically when this migration is applied.</summary>
    public required string Sql { get; init; }
}
