namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL for #348's backup quota warning, widening three CHECK constraints of
/// <c>System_Notification</c> at once: <c>MetadataKind</c> for <c>BackupQuotaReached</c>,
/// <c>DismissTriggerKey</c> for <c>BackupQuotaRestored</c>, and <c>Resolution</c> for <c>UnderQuota</c>.
/// Consumed by <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns the version number.
/// </summary>
public static class NotificationBackupQuotaMigrations
{
    /// <summary>
    /// Rebuilds the table with the three CHECKs widened, per ADR 008's enum-backed-column checklist.
    /// <para>
    /// One rebuild rather than three: all three columns are on the same table, SQLite has no
    /// <c>ALTER TABLE ... MODIFY CHECK</c>, and each widening would otherwise copy every row again. The
    /// shape is migration 23's (see <see cref="NotificationBackupRefusedMigrations"/> for why the column
    /// order, the indexes and the foreign key need nothing more), with only the three value lists changed.
    /// </para>
    /// </summary>
    public const string WidenForBackupQuotaWarning = """
        CREATE TABLE IF NOT EXISTS System_Notification_New (
            Id                TEXT    NOT NULL PRIMARY KEY,
            Type              TEXT    NOT NULL
                              CHECK (Type IN ('Information', 'Warning', 'Error', 'Success', 'ActionRequired')),
            Body              TEXT    NOT NULL,
            ExpiresAt         TEXT,
            IsDismissed       INTEGER NOT NULL DEFAULT 0,
            DismissedAt       TEXT,
            DismissTriggerKey TEXT
                              CHECK (DismissTriggerKey IS NULL OR DismissTriggerKey IN ('DatabaseReset', 'Reseed', 'ImportReviewResolved', 'BackupQuotaRestored')),
            DateCreated       TEXT    NOT NULL,
            DateModified      TEXT,
            DateDeleted       TEXT,
            IsDeleted         INTEGER NOT NULL DEFAULT 0,
            Title             TEXT,
            Metadata          TEXT,
            MetadataKind      TEXT
                              CHECK (MetadataKind IS NULL OR MetadataKind IN ('Announcement', 'SchemaVersionOvershoot', 'WhatsNew', 'ReseedRecommended', 'ReseedFileApplied', 'ImportReviewPending', 'BackupRefused', 'BackupQuotaReached')),
            AppVersionId      TEXT    REFERENCES System_AppVersion(Id),
            OriginalLanguage  TEXT    NOT NULL DEFAULT 'en',
            DismissReason     TEXT
                              CHECK (DismissReason IS NULL OR DismissReason IN ('Dismissed', 'Resolved', 'Obsolete')),
            Resolution        TEXT
                              CHECK (Resolution IS NULL OR Resolution IN ('KeptExisting', 'TookIncoming', 'Reseeded', 'Reset', 'UnderQuota'))
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