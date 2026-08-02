namespace Quotinator.Data.Database;

/// <summary>
/// #253/ADR 015 — renames every <c>Quotinator.Data</c>-owned table to its domain-prefixed final name
/// (<c>Import_</c>/<c>Audit_</c>). Consumed by <see cref="DatabaseInitializer.DataOwnedMigrations"/>
/// as version 5 — appended after versions 3/4 (<see cref="ImportConflictMigrations.AddAppliedPolicyCheckConstraint"/>,
/// <see cref="ImportActionMigrations.AddAppliedPolicyCheckConstraint"/>), not folded into them. An
/// earlier version of this migration squashed versions 3+4 into a new version 3 alongside this rename,
/// reasoning that neither had ever shipped in a tagged release — found live during #254's own T1 pass
/// (2026-08-02) to be wrong: this project's own local dev database had already run both in an earlier
/// session, so the squash silently skipped the entire rename on that database (its already-recorded
/// version 4 read as "up to date" under the new 3-migration count). See
/// <see cref="DatabaseInitializer.DataOwnedMigrations"/>'s own remarks for the full incident. Because
/// versions 3/4 always run before this one and already rebuild <c>System_ImportConflicts</c>/
/// <c>System_ImportActions</c> with the <c>AppliedPolicy</c> CHECK constraint applied (including the
/// legacy-casing normalisation), this migration's own rename step for those two tables is a plain
/// <c>ALTER TABLE ... RENAME TO</c> — no rebuild, no casing normalisation to repeat.
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
/// <c>Quotinator.Core</c>'s own migration 6 then copies <c>ImportBatches</c>' data into this table,
/// drops <c>ImportBatches</c>, and rebuilds the nine tables that FK-reference it — see
/// <c>Quotinator.Core.Database.QuotinatorMigrations.Migration006_DomainPrefixRename</c>'s own remarks
/// for why that half can't live here too (<c>Quotinator.Data</c> has no dependency on
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
    /// Creates <c>Import_Batch</c> fresh and empty (see the class's own remarks for why) and renames
    /// every other Data-owned table to its final domain-prefixed name via plain
    /// <c>ALTER TABLE ... RENAME TO</c> — no column or constraint changes needed for any of them,
    /// since versions 3/4 already rebuilt <c>System_ImportConflicts</c>/<c>System_ImportActions</c>
    /// with the <c>AppliedPolicy</c> CHECK constraint applied before this migration ever runs.
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

        ALTER TABLE System_ImportConflicts RENAME TO Import_Conflict;
        DROP INDEX IF EXISTS IX_System_ImportConflicts_BatchId;
        DROP INDEX IF EXISTS IX_System_ImportConflicts_Status;
        CREATE INDEX IF NOT EXISTS IX_Import_Conflict_BatchId ON Import_Conflict (BatchId);
        CREATE INDEX IF NOT EXISTS IX_Import_Conflict_Status ON Import_Conflict (Status);

        ALTER TABLE System_ImportActions RENAME TO Import_Action;
        DROP INDEX IF EXISTS IX_System_ImportActions_BatchId;
        DROP INDEX IF EXISTS IX_System_ImportActions_Status;
        CREATE INDEX IF NOT EXISTS IX_Import_Action_BatchId ON Import_Action (BatchId);
        CREATE INDEX IF NOT EXISTS IX_Import_Action_Status ON Import_Action (Status);
        """;
}
