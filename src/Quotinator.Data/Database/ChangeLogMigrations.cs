namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL scripts for the change-log table.
/// Consumed by <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns version numbers
/// and determines the sequence. Scripts here are version-agnostic — the order is the outer layer's concern.
/// </summary>
public static class ChangeLogMigrations
{
    /// <summary>
    /// Creates the <c>System_ChangeLog</c> table and its covering index, directly under its final
    /// <c>System_</c>-prefixed name. Like <see cref="ImportConflictMigrations"/>, no create-then-rename
    /// pair is needed — this table is introduced fresh, so it never existed under a legacy name.
    /// Idempotent: uses <c>CREATE TABLE IF NOT EXISTS</c> and <c>CREATE INDEX IF NOT EXISTS</c>. Carries
    /// <c>RecordBase</c>'s columns per ADR 002 — this table was still unreleased when that ADR gap was
    /// caught, so the shape was corrected here directly rather than via a follow-up migration.
    /// </summary>
    public const string CreateChangeLogTable = """
        CREATE TABLE IF NOT EXISTS System_ChangeLog (
            Id               TEXT NOT NULL PRIMARY KEY,
            EntityType       TEXT NOT NULL,
            EntityId         TEXT NOT NULL,
            InitiatedByType  TEXT NOT NULL
                             CHECK (InitiatedByType IN ('Seed','Import','WriteEndpoint','Enrichment')),
            InitiatedById    TEXT,
            Action           TEXT NOT NULL
                             CHECK (Action IN ('Created','Modified','SoftDelete','HardDelete')),
            Field            TEXT,
            OldValue         TEXT,
            NewValue         TEXT,
            OccurredAt       TEXT NOT NULL,
            DateCreated      TEXT NOT NULL,
            DateModified     TEXT,
            DateDeleted      TEXT,
            IsDeleted        INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS IX_System_ChangeLog_Entity ON System_ChangeLog (EntityType, EntityId, OccurredAt DESC);
        """;
}
