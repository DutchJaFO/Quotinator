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
    /// schema — so per ADR 008 both carry a matching CHECK constraint from creation.
    /// </summary>
    /// <remarks>
    /// This migration's SQL text is frozen — do not edit it in place, even for a change that looks
    /// self-contained. #165 initially assumed this table "has never been applied to any real
    /// database," which was true for every published release but not for developers' own existing
    /// local databases (whose <c>System_SchemaVersion</c> already recorded this migration's version
    /// as applied). Editing the text in place changed what a fresh database gets without changing
    /// what an already-migrated database gets, since the migration runner only compares version
    /// numbers, not content — <see cref="AddBlockedStatusAndMarkCompletenessAs"/> is the corrected,
    /// additive fix. See CLAUDE.md's migration policy: "once applied to a real database, a migration
    /// is frozen."
    /// </remarks>
    public const string CreateImportActionsTable = """
        CREATE TABLE IF NOT EXISTS System_ImportActions (
            Id              TEXT    NOT NULL PRIMARY KEY,
            BatchId         TEXT    NOT NULL,
            ActionType      TEXT    NOT NULL
                            CHECK (ActionType IN ('Add', 'Modify')),
            EntityType      TEXT    NOT NULL,
            EntityId        TEXT    NOT NULL,
            ExistingBatchId TEXT,
            ExistingValue   TEXT,
            IncomingValue   TEXT    NOT NULL,
            AppliedPolicy   TEXT,
            Status          TEXT    NOT NULL
                            CHECK (Status IN ('Pending', 'Decided', 'Applied', 'Discarded')),
            MergedFields    TEXT,
            DetectedAt      TEXT    NOT NULL,
            AppliedAt       TEXT,
            DiscardedAt     TEXT,
            DateCreated     TEXT    NOT NULL,
            DateModified    TEXT,
            DateDeleted     TEXT,
            IsDeleted       INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS IX_System_ImportActions_BatchId ON System_ImportActions (BatchId);
        CREATE INDEX IF NOT EXISTS IX_System_ImportActions_Status ON System_ImportActions (Status);
        """;

    /// <summary>
    /// #165: adds <c>Blocked</c> to <c>Status</c>'s CHECK constraint and a new
    /// <c>MarkCompletenessAs</c> column. Widening an existing column's CHECK constraint isn't
    /// something SQLite's <c>ALTER TABLE</c> supports directly, so this rebuilds the table under a
    /// temporary name (same technique as <c>Migration004_ImportBatchTypeUserSeed</c> in
    /// <c>QuotinatorMigrations</c>) rather than editing <see cref="CreateImportActionsTable"/> in
    /// place. <c>MarkCompletenessAs</c> could technically have been added via a plain <c>ALTER TABLE
    /// ... ADD COLUMN</c> (ADR 008 confirms that supports an inline CHECK), but since the table
    /// already needs a full rebuild for the <c>Status</c> change, both go in the same rebuild rather
    /// than two separate statements.
    /// </summary>
    public const string AddBlockedStatusAndMarkCompletenessAs = """
        CREATE TABLE System_ImportActions_New (
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

        INSERT INTO System_ImportActions_New (
            Id, BatchId, ActionType, EntityType, EntityId, ExistingBatchId, ExistingValue,
            IncomingValue, AppliedPolicy, Status, MergedFields, DetectedAt, AppliedAt, DiscardedAt,
            DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT
            Id, BatchId, ActionType, EntityType, EntityId, ExistingBatchId, ExistingValue,
            IncomingValue, AppliedPolicy, Status, MergedFields, DetectedAt, AppliedAt, DiscardedAt,
            DateCreated, DateModified, DateDeleted, IsDeleted
        FROM System_ImportActions;

        DROP TABLE System_ImportActions;

        ALTER TABLE System_ImportActions_New RENAME TO System_ImportActions;

        CREATE INDEX IF NOT EXISTS IX_System_ImportActions_BatchId ON System_ImportActions (BatchId);
        CREATE INDEX IF NOT EXISTS IX_System_ImportActions_Status ON System_ImportActions (Status);
        """;
}
