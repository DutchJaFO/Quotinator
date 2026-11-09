namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL for #482's notification, widening <c>System_Notification</c>'s
/// <c>MetadataKind</c> CHECK for <c>UnreadableDatabaseSetAside</c>. Consumed by
/// <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns the version number.
/// </summary>
public static class NotificationUnreadableDatabaseMigrations
{
    /// <summary>
    /// Rebuilds the table with <c>MetadataKind</c>'s CHECK widened, per ADR 008's enum-backed-column
    /// checklist: SQLite has no <c>ALTER TABLE ... MODIFY CHECK</c>, so widening an existing one means a
    /// rebuild. The notification reports an event and nothing resolves it, so the dismiss-trigger and
    /// resolution CHECKs are carried over unchanged.
    /// <para>
    /// The table shape is the one migration 28 left behind, with only the one value list extended; the
    /// schema-drift tests hold that claim, by comparing this replayed result against the baseline.
    /// </para>
    /// </summary>
    public const string WidenMetadataKindForUnreadableDatabase = """
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
                              CHECK (MetadataKind IS NULL OR MetadataKind IN ('Announcement', 'SchemaVersionOvershoot', 'WhatsNew', 'ReseedRecommended', 'ReseedFileApplied', 'ImportReviewPending', 'BackupRefused', 'BackupQuotaReached', 'BackupMaxExceeded', 'DatabaseCreated', 'MigrationApplied', 'UnreadableDatabaseSetAside')),
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
