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
}
