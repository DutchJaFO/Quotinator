namespace Quotinator.Data.Database;

/// <summary>
/// #253/ADR 015 — renames every <c>Quotinator.Data</c>-owned table to its domain-prefixed final name
/// (<c>Import_</c>/<c>Audit_</c>), folding in the <c>AppliedPolicy</c> CHECK constraint work that was
/// previously two separate, unreleased migrations (<see cref="ImportConflictMigrations.AddAppliedPolicyCheckConstraint"/>,
/// <see cref="ImportActionMigrations.AddAppliedPolicyCheckConstraint"/>) — safe to squash per this
/// project's migration policy, since neither had ever shipped. Consumed by
/// <see cref="DatabaseInitializer.DataOwnedMigrations"/>.
/// </summary>
/// <remarks>
/// <c>Import_Batch</c> is created fresh here (<c>CREATE TABLE IF NOT EXISTS</c>, empty) rather than
/// renamed from <c>ImportBatches</c> — ADR 015 classifies it as Data-owned, but
/// <c>Quotinator.Core</c>'s own migration 3 is what physically creates <c>ImportBatches</c>, and
/// Data's entire migration phase always runs to completion before Consumer's phase starts, with no
/// interleaving possible (<see cref="DatabaseInitializer.ApplyMigrationsAsync"/>) — a rename attempted
/// here would run before that table exists on a genuinely fresh incremental replay. Creating the final
/// table empty sidesteps the ordering problem entirely: it works identically whether
/// <c>ImportBatches</c> already exists (a real upgrade) or doesn't yet (a fresh incremental replay).
/// <c>Quotinator.Core</c>'s own migration then copies <c>ImportBatches</c>' data into this table,
/// drops <c>ImportBatches</c>, and rebuilds the nine tables that FK-reference it — see
/// <c>Quotinator.Core.Database.QuotinatorMigrations.Migration005_ImportBatchConflictPolicyCheckConstraint</c>'s
/// own remarks for why that half can't live here too (<c>Quotinator.Data</c> has no dependency on
/// <c>Quotinator.Core</c> per ADR 004, so this can only be a text reference, not a <c>cref</c>).
/// SQLite only auto-updates a FOREIGN KEY
/// declaration in another table when the referenced table is renamed via
/// <c>ALTER TABLE ... RENAME TO</c>, not when it's dropped and a differently-named table created in
/// its place — confirmed against sqlite.org — so the nine rebuilds must happen wherever
/// <c>ImportBatches</c>' data lands, which is Core's migration, immediately after this one).
/// </remarks>
public static class DomainPrefixRenameMigrations
{
    /// <summary>
    /// Creates <c>Import_Batch</c> fresh and empty (see the class's own remarks for why), renames
    /// <c>System_AuditEntries</c>/<c>System_ChangeLog</c>/<c>System_SourceFileOverrides</c>
    /// (plain <c>ALTER TABLE ... RENAME TO</c> where no column changes are needed), and rebuilds
    /// <c>System_ImportConflicts</c>/<c>System_ImportActions</c> under their final names with the
    /// <c>AppliedPolicy</c> CHECK constraint applied in the same step (SQLite has no
    /// <c>ALTER TABLE ... ADD CHECK</c>, and a rebuild was already required for that constraint, so it
    /// is combined with the rename rather than done as two separate steps).
    /// </summary>
    public const string RenameDataOwnedTables = """
        CREATE TABLE IF NOT EXISTS Import_Batch (
            Id             TEXT    PRIMARY KEY,
            Name           TEXT    NOT NULL,
            Type           TEXT    NOT NULL CHECK (Type IN ('Seed', 'Import', 'System', 'UserSeed')),
            Url            TEXT,
            ImportedAt     TEXT    NOT NULL,
            ImportedById   TEXT,
            RecordCount    INTEGER NOT NULL DEFAULT 0,
            DateCreated    TEXT    NOT NULL,
            DateModified   TEXT,
            DateDeleted    TEXT,
            IsDeleted      INTEGER NOT NULL DEFAULT 0,
            ConflictPolicy TEXT    NOT NULL DEFAULT 'Skip'
                           CHECK (ConflictPolicy IN ('Skip', 'NewestWins', 'MergeOurs', 'MergeTheirs', 'Review')),
            Status         TEXT    NOT NULL DEFAULT 'Applied'
                           CHECK (Status IN ('Staged', 'Applied', 'Discarded')),
            AppliedAt      TEXT
        );

        ALTER TABLE System_AuditEntries RENAME TO Audit_Entry;
        DROP INDEX IF EXISTS IX_System_AuditEntries_TableName_RecordId;
        DROP INDEX IF EXISTS IX_System_AuditEntries_PerformedAt;
        CREATE INDEX IF NOT EXISTS IX_Audit_Entry_TableName_RecordId ON Audit_Entry (TableName, RecordId);
        CREATE INDEX IF NOT EXISTS IX_Audit_Entry_PerformedAt ON Audit_Entry (PerformedAt);

        ALTER TABLE System_ChangeLog RENAME TO Audit_Change;
        DROP INDEX IF EXISTS IX_System_ChangeLog_Entity;
        CREATE INDEX IF NOT EXISTS IX_Audit_Change_Entity ON Audit_Change (EntityType, EntityId, OccurredAt DESC);

        ALTER TABLE System_SourceFileOverrides RENAME TO Import_SourceFileOverride;
        DROP INDEX IF EXISTS UX_System_SourceFileOverrides_FileName_Origin;
        CREATE UNIQUE INDEX IF NOT EXISTS UX_Import_SourceFileOverride_FileName_Origin
            ON Import_SourceFileOverride (FileName, Origin) WHERE IsDeleted = 0;

        CREATE TABLE Import_Conflict (
            Id              TEXT    NOT NULL PRIMARY KEY,
            BatchId         TEXT    NOT NULL,
            EntityType      TEXT    NOT NULL,
            EntityId        TEXT,
            ExistingValue   TEXT,
            IncomingValue   TEXT,
            AppliedPolicy   TEXT
                            CHECK (AppliedPolicy IS NULL OR AppliedPolicy IN ('Skip', 'NewestWins', 'MergeOurs', 'MergeTheirs', 'Review')),
            Status          TEXT    NOT NULL
                            CHECK (Status IN ('Pending', 'Decided', 'Resolved')),
            MergedFields    TEXT,
            DetectedAt      TEXT    NOT NULL,
            ResolvedAt      TEXT,
            DateCreated     TEXT    NOT NULL,
            DateModified    TEXT,
            DateDeleted     TEXT,
            IsDeleted       INTEGER NOT NULL DEFAULT 0,
            ExistingBatchId TEXT
        );

        INSERT INTO Import_Conflict (Id, BatchId, EntityType, EntityId, ExistingValue, IncomingValue, AppliedPolicy, Status, MergedFields, DetectedAt, ResolvedAt, DateCreated, DateModified, DateDeleted, IsDeleted, ExistingBatchId)
        SELECT
            Id, BatchId, EntityType, EntityId, ExistingValue, IncomingValue,
            CASE AppliedPolicy
                WHEN 'skip'         THEN 'Skip'
                WHEN 'newest-wins'  THEN 'NewestWins'
                WHEN 'merge-ours'   THEN 'MergeOurs'
                WHEN 'merge-theirs' THEN 'MergeTheirs'
                WHEN 'review'       THEN 'Review'
                ELSE AppliedPolicy
            END,
            Status, MergedFields, DetectedAt, ResolvedAt, DateCreated, DateModified, DateDeleted, IsDeleted, ExistingBatchId
        FROM System_ImportConflicts;

        DROP TABLE System_ImportConflicts;

        CREATE INDEX IF NOT EXISTS IX_Import_Conflict_BatchId ON Import_Conflict (BatchId);
        CREATE INDEX IF NOT EXISTS IX_Import_Conflict_Status ON Import_Conflict (Status);

        CREATE TABLE Import_Action (
            Id                 TEXT    NOT NULL PRIMARY KEY,
            BatchId            TEXT    NOT NULL,
            ActionType         TEXT    NOT NULL
                               CHECK (ActionType IN ('Add', 'Modify')),
            EntityType         TEXT    NOT NULL,
            EntityId           TEXT    NOT NULL,
            ExistingBatchId    TEXT,
            ExistingValue      TEXT,
            IncomingValue      TEXT    NOT NULL,
            AppliedPolicy      TEXT
                               CHECK (AppliedPolicy IS NULL OR AppliedPolicy IN ('Skip', 'NewestWins', 'MergeOurs', 'MergeTheirs', 'Review')),
            Status             TEXT    NOT NULL
                               CHECK (Status IN ('Pending', 'Decided', 'Applied', 'Discarded', 'Blocked', 'Stale')),
            MergedFields       TEXT,
            MarkCompletenessAs TEXT
                               CHECK (MarkCompletenessAs IS NULL OR MarkCompletenessAs IN ('Incomplete', 'NeedsReview', 'Complete')),
            DetectedAt         TEXT    NOT NULL,
            AppliedAt          TEXT,
            DiscardedAt        TEXT,
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0,
            OriginalDecision   TEXT
        );

        INSERT INTO Import_Action (
            Id, BatchId, ActionType, EntityType, EntityId, ExistingBatchId, ExistingValue,
            IncomingValue, AppliedPolicy, Status, MergedFields, MarkCompletenessAs, DetectedAt,
            AppliedAt, DiscardedAt, DateCreated, DateModified, DateDeleted, IsDeleted, OriginalDecision)
        SELECT
            Id, BatchId, ActionType, EntityType, EntityId, ExistingBatchId, ExistingValue, IncomingValue,
            CASE AppliedPolicy
                WHEN 'skip'         THEN 'Skip'
                WHEN 'newest-wins'  THEN 'NewestWins'
                WHEN 'merge-ours'   THEN 'MergeOurs'
                WHEN 'merge-theirs' THEN 'MergeTheirs'
                WHEN 'review'       THEN 'Review'
                ELSE AppliedPolicy
            END,
            Status, MergedFields, MarkCompletenessAs, DetectedAt, AppliedAt, DiscardedAt,
            DateCreated, DateModified, DateDeleted, IsDeleted, OriginalDecision
        FROM System_ImportActions;

        DROP TABLE System_ImportActions;

        CREATE INDEX IF NOT EXISTS IX_Import_Action_BatchId ON Import_Action (BatchId);
        CREATE INDEX IF NOT EXISTS IX_Import_Action_Status ON Import_Action (Status);
        """;
}
