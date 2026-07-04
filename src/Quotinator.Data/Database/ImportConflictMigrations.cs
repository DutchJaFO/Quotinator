namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL scripts for the import-conflicts table.
/// Consumed by <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns version numbers
/// and determines the sequence. Scripts here are version-agnostic — the order is the outer layer's concern.
/// </summary>
public static class ImportConflictMigrations
{
    /// <summary>
    /// Creates the <c>System_ImportConflicts</c> table and its covering indexes, directly under its
    /// final <c>System_</c>-prefixed name. Unlike <see cref="AuditMigrations"/>, no create-then-rename
    /// pair is needed — this table is introduced fresh, so it never existed under a legacy name.
    /// Idempotent: uses <c>CREATE TABLE IF NOT EXISTS</c> and <c>CREATE INDEX IF NOT EXISTS</c>.
    /// </summary>
    public const string CreateImportConflictsTable = """
        CREATE TABLE IF NOT EXISTS System_ImportConflicts (
            Id            INTEGER PRIMARY KEY AUTOINCREMENT,
            BatchId       TEXT    NOT NULL,
            EntityType    TEXT    NOT NULL,
            EntityId      TEXT,
            ExistingValue TEXT,
            IncomingValue TEXT,
            AppliedPolicy TEXT,
            Status        TEXT    NOT NULL,
            MergedFields  TEXT,
            DetectedAt    TEXT    NOT NULL,
            ResolvedAt    TEXT
        );
        CREATE INDEX IF NOT EXISTS IX_System_ImportConflicts_BatchId ON System_ImportConflicts (BatchId);
        CREATE INDEX IF NOT EXISTS IX_System_ImportConflicts_Status ON System_ImportConflicts (Status);
        """;
}
