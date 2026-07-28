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
    /// Frozen as originally shipped (<c>Id INTEGER PRIMARY KEY AUTOINCREMENT</c>, no <c>RecordBase</c>
    /// columns) — this migration had already applied to real local databases by the time ADR 002's
    /// gap was caught, so per this project's migration policy it can never be edited in place. The
    /// retrofit onto <c>RecordBase</c> is a separate, later migration: <see cref="MigrateToRecordBase"/>.
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

    /// <summary>
    /// Retrofits <c>System_ImportConflicts</c> onto <c>RecordBase</c>'s shape, per ADR 002
    /// ("RecordBase applies to all tables without exception"). This table's original creation
    /// migration (<see cref="CreateImportConflictsTable"/>) had already applied to real local
    /// databases with an <c>INTEGER AUTOINCREMENT</c> primary key, so the column-type change
    /// (<c>long</c> -&gt; Guid <c>TEXT</c>) can't be made in place; SQLite has no <c>ALTER TABLE</c>
    /// form for changing a column's type or PK behaviour. Rebuilds the table under a temporary name
    /// (same technique as <c>Migration004_ImportBatchTypeUserSeed</c> and
    /// <see cref="AuditMigrations.MigrateToRecordBase"/>), generating a synthetic Guid per existing
    /// row — SQLite has no native UUID function, so one is assembled from <c>randomblob</c>/<c>hex</c>
    /// in the same 8-4-4-4-12 grouping <c>Guid.Parse</c> expects. <c>DateCreated</c> backfills from
    /// <c>DetectedAt</c> (the closest available approximation for existing rows);
    /// <c>DateModified</c>/<c>DateDeleted</c> stay <c>NULL</c> and <c>IsDeleted</c> defaults to 0.
    /// </summary>
    public const string MigrateToRecordBase = """
        CREATE TABLE IF NOT EXISTS System_ImportConflicts_New (
            Id            TEXT    NOT NULL PRIMARY KEY,
            BatchId       TEXT    NOT NULL,
            EntityType    TEXT    NOT NULL,
            EntityId      TEXT,
            ExistingValue TEXT,
            IncomingValue TEXT,
            AppliedPolicy TEXT,
            Status        TEXT    NOT NULL,
            MergedFields  TEXT,
            DetectedAt    TEXT    NOT NULL,
            ResolvedAt    TEXT,
            DateCreated   TEXT    NOT NULL,
            DateModified  TEXT,
            DateDeleted   TEXT,
            IsDeleted     INTEGER NOT NULL DEFAULT 0
        );

        INSERT INTO System_ImportConflicts_New (Id, BatchId, EntityType, EntityId, ExistingValue, IncomingValue, AppliedPolicy, Status, MergedFields, DetectedAt, ResolvedAt, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT
            lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' ||
                lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6))),
            BatchId, EntityType, EntityId, ExistingValue, IncomingValue, AppliedPolicy, Status, MergedFields, DetectedAt, ResolvedAt, DetectedAt, NULL, NULL, 0
        FROM System_ImportConflicts;

        DROP TABLE System_ImportConflicts;

        ALTER TABLE System_ImportConflicts_New RENAME TO System_ImportConflicts;

        CREATE INDEX IF NOT EXISTS IX_System_ImportConflicts_BatchId ON System_ImportConflicts (BatchId);
        CREATE INDEX IF NOT EXISTS IX_System_ImportConflicts_Status ON System_ImportConflicts (Status);
        """;

    /// <summary>
    /// Adds <c>ExistingBatchId</c> — the batch that originally created the <i>existing</i> side of a
    /// conflict, distinct from <c>BatchId</c> (which has always meant the batch during which the
    /// conflict was <i>detected</i>, i.e. the incoming side). Needed for #149's manual conflict-review
    /// workflow to label which side is which, and to detect a conflict between two entries in the same
    /// imported file (<c>ExistingBatchId == BatchId</c>). A plain <c>ALTER TABLE ... ADD COLUMN</c> is
    /// sufficient here — no rebuild needed, since this only adds a new nullable column to a table
    /// already in its final <c>RecordBase</c> shape (see <see cref="MigrateToRecordBase"/>).
    /// </summary>
    public const string AddExistingBatchId = """
        ALTER TABLE System_ImportConflicts ADD COLUMN ExistingBatchId TEXT;
        """;

    /// <summary>
    /// Adds a CHECK constraint to <c>Status</c>, per ADR 008 (<c>ImportConflictStatus</c> is a real
    /// C# enum — a closed set defined and maintained entirely by this project's own coordinator
    /// logic, not by any consuming project's schema) and ADR 009 (verify against what a real
    /// database actually contains, not just fresh-database assumptions). This column's migrations
    /// (<see cref="MigrateToRecordBase"/>, <see cref="AddExistingBatchId"/>) had already applied to
    /// real local databases by the time the enum-conversion gap was caught, so — same as
    /// <c>Migration004_ImportBatchTypeUserSeed</c> — the table is rebuilt under a temporary name
    /// rather than edited in place; SQLite has no <c>ALTER TABLE ... ADD CHECK</c>.
    /// <para>
    /// Existing rows predate the enum conversion and store the original lowercase values
    /// (<c>"pending"</c>/<c>"decided"</c>/<c>"resolved"</c>, from the pre-enum string constants);
    /// new code now writes <c>ImportConflictStatus.X.ToString()</c> (PascalCase). The rebuild's
    /// copy step normalises old lowercase values to the new PascalCase form so existing rows satisfy
    /// the new CHECK — <c>ELSE Status</c> passes through anything already PascalCase (a fresh
    /// database that never had the old lowercase data) or any genuinely unexpected value, which then
    /// correctly fails the CHECK rather than being silently miscategorised.
    /// </para>
    /// </summary>
    public const string AddStatusCheckConstraint = """
        CREATE TABLE IF NOT EXISTS System_ImportConflicts_New (
            Id              TEXT    NOT NULL PRIMARY KEY,
            BatchId         TEXT    NOT NULL,
            EntityType      TEXT    NOT NULL,
            EntityId        TEXT,
            ExistingValue   TEXT,
            IncomingValue   TEXT,
            AppliedPolicy   TEXT,
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

        INSERT INTO System_ImportConflicts_New (Id, BatchId, EntityType, EntityId, ExistingValue, IncomingValue, AppliedPolicy, Status, MergedFields, DetectedAt, ResolvedAt, DateCreated, DateModified, DateDeleted, IsDeleted, ExistingBatchId)
        SELECT
            Id, BatchId, EntityType, EntityId, ExistingValue, IncomingValue, AppliedPolicy,
            CASE Status
                WHEN 'pending'  THEN 'Pending'
                WHEN 'decided'  THEN 'Decided'
                WHEN 'resolved' THEN 'Resolved'
                ELSE Status
            END,
            MergedFields, DetectedAt, ResolvedAt, DateCreated, DateModified, DateDeleted, IsDeleted, ExistingBatchId
        FROM System_ImportConflicts;

        DROP TABLE System_ImportConflicts;

        ALTER TABLE System_ImportConflicts_New RENAME TO System_ImportConflicts;

        CREATE INDEX IF NOT EXISTS IX_System_ImportConflicts_BatchId ON System_ImportConflicts (BatchId);
        CREATE INDEX IF NOT EXISTS IX_System_ImportConflicts_Status ON System_ImportConflicts (Status);
        """;
}
