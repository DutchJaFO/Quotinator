namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL scripts for the import-actions table (#154).
/// Consumed by <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns version numbers
/// and determines the sequence. Scripts here are version-agnostic — the order is the outer layer's concern.
/// </summary>
public static class ImportActionMigrations
{
    /// <summary>
    /// Creates the <c>System_ImportActions</c> table and its covering indexes, directly under its
    /// final RecordBase Guid-keyed shape. Unlike <see cref="ImportConflictMigrations"/>, this table
    /// is introduced fresh after ADR 002 was already established, so no create-then-retrofit pair is
    /// needed. Idempotent: uses <c>CREATE TABLE IF NOT EXISTS</c> and <c>CREATE INDEX IF NOT EXISTS</c>.
    /// <c>Status</c> and <c>ActionType</c> are both backed by real C# enums — closed sets defined and
    /// maintained entirely by this project's own coordinator logic, not by any consuming project's
    /// schema — so per ADR 008 both carry a matching CHECK constraint from creation. This migration
    /// has never been applied to any real database (only ephemeral test databases), so the CHECK was
    /// added directly here rather than via a separate follow-up migration. Same reasoning applies to
    /// <c>Blocked</c> (#165) and <c>MarkCompletenessAs</c> (#165) being added directly here rather
    /// than via a follow-up migration.
    /// </summary>
    public const string CreateImportActionsTable = """
        CREATE TABLE IF NOT EXISTS System_ImportActions (
            Id                 TEXT    NOT NULL PRIMARY KEY,
            BatchId            TEXT    NOT NULL,
            ActionType         TEXT    NOT NULL
                               CHECK (ActionType IN ('Add', 'Modify')),
            EntityType         TEXT    NOT NULL,
            EntityId           TEXT    NOT NULL,
            ExistingBatchId    TEXT,
            ExistingValue      TEXT,
            IncomingValue      TEXT    NOT NULL,
            AppliedPolicy      TEXT,
            Status             TEXT    NOT NULL
                               CHECK (Status IN ('Pending', 'Decided', 'Applied', 'Discarded', 'Blocked')),
            MergedFields       TEXT,
            MarkCompletenessAs TEXT
                               CHECK (MarkCompletenessAs IS NULL OR MarkCompletenessAs IN ('Incomplete', 'NeedsReview', 'Complete')),
            DetectedAt         TEXT    NOT NULL,
            AppliedAt          TEXT,
            DiscardedAt        TEXT,
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS IX_System_ImportActions_BatchId ON System_ImportActions (BatchId);
        CREATE INDEX IF NOT EXISTS IX_System_ImportActions_Status ON System_ImportActions (Status);
        """;
}
