namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL scripts for the audit tables.
/// Consumed by <c>QuotinatorMigrations</c> in <c>Quotinator.Core</c>, which assigns version numbers
/// and determines the sequence. Scripts here are version-agnostic — the order is the outer layer's concern.
/// </summary>
public static class AuditMigrations
{
    /// <summary>
    /// Creates the <c>AuditEntries</c> table and its covering indexes.
    /// Idempotent: uses <c>CREATE TABLE IF NOT EXISTS</c> and <c>CREATE INDEX IF NOT EXISTS</c>.
    /// </summary>
    public const string CreateAuditEntriesTable = """
        CREATE TABLE IF NOT EXISTS AuditEntries (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            TableName   TEXT    NOT NULL,
            RecordId    TEXT,
            Operation   TEXT    NOT NULL,
            Agent       TEXT,
            PerformedAt TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_AuditEntries_TableName_RecordId ON AuditEntries (TableName, RecordId);
        CREATE INDEX IF NOT EXISTS IX_AuditEntries_PerformedAt ON AuditEntries (PerformedAt);
        """;

    /// <summary>
    /// Renames <c>AuditEntries</c> to <c>System_AuditEntries</c> (and its two indexes) so the table
    /// is recognised as protected system data by <c>Sql.Schema.GetUserTables</c>'s generic, escaped
    /// <c>System\_%</c> pattern match.
    /// </summary>
    public const string RenameAuditEntriesToSystemAuditEntries = """
        ALTER TABLE AuditEntries RENAME TO System_AuditEntries;
        DROP INDEX IF EXISTS IX_AuditEntries_TableName_RecordId;
        DROP INDEX IF EXISTS IX_AuditEntries_PerformedAt;
        CREATE INDEX IF NOT EXISTS IX_System_AuditEntries_TableName_RecordId ON System_AuditEntries (TableName, RecordId);
        CREATE INDEX IF NOT EXISTS IX_System_AuditEntries_PerformedAt ON System_AuditEntries (PerformedAt);
        """;

    /// <summary>
    /// Retrofits <c>System_AuditEntries</c> onto <c>RecordBase</c>'s shape, per ADR 002 ("RecordBase
    /// applies to all tables without exception"), which predates this table's original implementation
    /// by a week but was never applied to it. Already shipped in v1.7.2 with an
    /// <c>INTEGER AUTOINCREMENT</c> primary key, so the column-type change (<c>long</c> -&gt; Guid
    /// <c>TEXT</c>) can't be made in place; SQLite has no <c>ALTER TABLE</c> form for changing a
    /// column's type or PK behaviour. Rebuilds the table under a temporary name (same technique as
    /// <c>Migration004_ImportBatchTypeUserSeed</c>), generating a synthetic Guid per existing row —
    /// SQLite has no native UUID function, so one is assembled from <c>randomblob</c>/<c>hex</c> in the
    /// same 8-4-4-4-12 grouping <c>Guid.Parse</c> expects. <c>DateCreated</c> backfills from
    /// <c>PerformedAt</c> (the closest available approximation for existing rows);
    /// <c>DateModified</c>/<c>DateDeleted</c> stay <c>NULL</c> and <c>IsDeleted</c> defaults to 0,
    /// since no audit entry has ever actually been modified or soft-deleted.
    /// </summary>
    public const string MigrateToRecordBase = """
        CREATE TABLE IF NOT EXISTS System_AuditEntries_New (
            Id           TEXT    NOT NULL PRIMARY KEY,
            TableName    TEXT    NOT NULL,
            RecordId     TEXT,
            Operation    TEXT    NOT NULL,
            Agent        TEXT,
            PerformedAt  TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0
        );

        INSERT INTO System_AuditEntries_New (Id, TableName, RecordId, Operation, Agent, PerformedAt, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT
            lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' ||
                lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6))),
            TableName, RecordId, Operation, Agent, PerformedAt, PerformedAt, NULL, NULL, 0
        FROM System_AuditEntries;

        DROP TABLE System_AuditEntries;

        ALTER TABLE System_AuditEntries_New RENAME TO System_AuditEntries;

        CREATE INDEX IF NOT EXISTS IX_System_AuditEntries_TableName_RecordId ON System_AuditEntries (TableName, RecordId);
        CREATE INDEX IF NOT EXISTS IX_System_AuditEntries_PerformedAt ON System_AuditEntries (PerformedAt);
        """;
}
