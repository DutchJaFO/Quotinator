namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL for #348's backup maximum error, widening three CHECK constraints of
/// <c>System_Notification</c> at once: <c>MetadataKind</c> for <c>BackupMaxExceeded</c>,
/// <c>DismissTriggerKey</c> for <c>BackupBackUnderMax</c>, and <c>Resolution</c> for <c>UnderMax</c>.
/// Consumed by <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns the version number.
/// </summary>
public static class NotificationBackupMaxMigrations
{
    /// <summary>
    /// Rebuilds the table with the three CHECKs widened, per ADR 008's enum-backed-column checklist:
    /// SQLite has no <c>ALTER TABLE ... MODIFY CHECK</c>, so widening an existing one means a rebuild.
    /// <para>
    /// One rebuild rather than three, since all three columns are on the same table and each widening
    /// would otherwise copy every row again. The table shape is the one the migration before this left
    /// behind, with only the three value lists extended; the schema-drift tests hold that claim, by
    /// comparing this replayed result against the baseline.
    /// </para>
    /// </summary>
    public const string WidenForBackupMaxError = """
        CREATE TABLE IF NOT EXISTS System_Notification_New (
            Id                TEXT    NOT NULL PRIMARY KEY,
            Type              TEXT    NOT NULL
                              CHECK (Type IN ('Information', 'Warning', 'Error', 'Success', 'ActionRequired')),
            Body              TEXT    NOT NULL,
            ExpiresAt         TEXT,
            IsDismissed       INTEGER NOT NULL DEFAULT 0,
            DismissedAt       TEXT,
            DismissTriggerKey TEXT
                              CHECK (DismissTriggerKey IS NULL OR DismissTriggerKey IN ('DatabaseReset', 'Reseed', 'ImportReviewResolved', 'BackupQuotaRestored', 'BackupBackUnderMax')),
            DateCreated       TEXT    NOT NULL,
            DateModified      TEXT,
            DateDeleted       TEXT,
            IsDeleted         INTEGER NOT NULL DEFAULT 0,
            Title             TEXT,
            Metadata          TEXT,
            MetadataKind      TEXT
                              CHECK (MetadataKind IS NULL OR MetadataKind IN ('Announcement', 'SchemaVersionOvershoot', 'WhatsNew', 'ReseedRecommended', 'ReseedFileApplied', 'ImportReviewPending', 'BackupRefused', 'BackupQuotaReached', 'BackupMaxExceeded')),
            AppVersionId      TEXT    REFERENCES System_AppVersion(Id),
            OriginalLanguage  TEXT    NOT NULL DEFAULT 'en',
            DismissReason     TEXT
                              CHECK (DismissReason IS NULL OR DismissReason IN ('Dismissed', 'Resolved', 'Obsolete')),
            Resolution        TEXT
                              CHECK (Resolution IS NULL OR Resolution IN ('KeptExisting', 'TookIncoming', 'Reseeded', 'Reset', 'UnderQuota', 'UnderMax'))
        );

        INSERT INTO System_Notification_New (
            Id, Type, Body, ExpiresAt, IsDismissed, DismissedAt, DismissTriggerKey,
            DateCreated, DateModified, DateDeleted, IsDeleted,
            Title, Metadata, MetadataKind, AppVersionId, OriginalLanguage, DismissReason, Resolution)
        SELECT
            Id, Type, Body, ExpiresAt, IsDismissed, DismissedAt, DismissTriggerKey,
            DateCreated, DateModified, DateDeleted, IsDeleted,
            Title, Metadata, MetadataKind, AppVersionId, OriginalLanguage, DismissReason, Resolution
        FROM System_Notification;

        DROP TABLE System_Notification;

        ALTER TABLE System_Notification_New RENAME TO System_Notification;

        CREATE INDEX IF NOT EXISTS IX_System_Notification_Active ON System_Notification (IsDismissed, IsDeleted, ExpiresAt);
        CREATE INDEX IF NOT EXISTS IX_System_Notification_DismissTriggerKey ON System_Notification (DismissTriggerKey);
        """;
}
