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

    /// <summary>
    /// What this migration did, in one sentence an operator can read (#371).
    /// </summary>
    /// <remarks>
    /// <see langword="required"/> is the point: neither this record nor <c>System_SchemaVersion</c>
    /// could previously say anything but <c>migration 27 applied</c>, and making the description
    /// unskippable is what stops the next migration being added without one.
    /// <para>
    /// This does not breach the migration-freeze rule, which governs <see cref="Sql"/>: that stays
    /// byte-identical, and a description changes nothing a database has already applied.
    /// </para>
    /// </remarks>
    public required string Description { get; init; }
}
