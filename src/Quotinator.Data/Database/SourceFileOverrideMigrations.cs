namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL scripts for the <c>System_SourceFileOverrides</c> table (#153).
/// Consumed by <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns version numbers
/// and determines the sequence.
/// </summary>
public static class SourceFileOverrideMigrations
{
    /// <summary>
    /// Creates the <c>System_SourceFileOverrides</c> table directly under its final RecordBase
    /// Guid-keyed shape — introduced fresh after ADR 002 was already established, so no
    /// create-then-retrofit pair is needed. <c>Origin</c> is backed by a real C# enum
    /// (<see cref="Import.SeedBatchOrigin"/>) — a closed set, so per ADR 008 it carries a matching
    /// CHECK constraint from creation. One row per (<c>FileName</c>, <c>Origin</c>) pair — enforced
    /// by a partial unique index so a soft-deleted row never blocks re-registering the same file.
    /// </summary>
    public const string CreateSourceFileOverridesTable = """
        CREATE TABLE IF NOT EXISTS System_SourceFileOverrides (
            Id            TEXT    NOT NULL PRIMARY KEY,
            FileName      TEXT    NOT NULL,
            Origin        TEXT    NOT NULL
                          CHECK (Origin IN ('Bundled', 'UserImports')),
            ContentHash   TEXT    NOT NULL,
            SourceBatchId TEXT,
            DateCreated   TEXT    NOT NULL,
            DateModified  TEXT,
            DateDeleted   TEXT,
            IsDeleted     INTEGER NOT NULL DEFAULT 0
        );
        CREATE UNIQUE INDEX IF NOT EXISTS UX_System_SourceFileOverrides_FileName_Origin
            ON System_SourceFileOverrides (FileName, Origin) WHERE IsDeleted = 0;
        """;
}
