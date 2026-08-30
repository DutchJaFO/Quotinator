namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL widening <c>System_Notification</c>'s two enum-backed CHECK constraints for
/// #304 — <c>DismissTriggerKey</c> gains <c>Reseed</c> and <c>MetadataKind</c> gains
/// <c>ReseedRecommended</c>. Consumed by <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which
/// assigns the version number.
/// </summary>
public static class NotificationReseedTriggerMigrations
{
    /// <summary>
    /// Rebuilds the table with both CHECKs widened, per ADR 008's enum-backed-column checklist.
    /// <para>
    /// A rebuild rather than <c>ALTER TABLE</c>: SQLite has no <c>ALTER TABLE ... MODIFY CHECK</c>
    /// (verified against sqlite.org), so widening an existing CHECK means create-new + copy + drop +
    /// rename. Both widenings ride in this one rebuild rather than taking a rebuild each — they are two
    /// constraints on the same table, and doing them separately would copy every row twice for no gain.
    /// </para>
    /// <para>
    /// Column order is preserved exactly as the incremental path produced it — <c>Title</c>,
    /// <c>Metadata</c>, <c>MetadataKind</c> and <c>AppVersionId</c> trail <c>IsDeleted</c> because
    /// migration 5 added them by <c>ALTER TABLE</c>, and <c>OriginalLanguage</c> trails them because
    /// migration 12 did the same. The baseline/incremental parity test compares column ordinals, so a
    /// "tidier" ordering here would fail it — and would be a schema change nobody asked for.
    /// </para>
    /// <para>
    /// The indexes are recreated because <c>DROP TABLE</c> takes them with it. Foreign keys are not
    /// disabled around the rename: <c>System_NotificationTranslation.NotificationId</c> references this
    /// table, and SQLite's default <c>legacy_alter_table=OFF</c> behaviour repoints such references to
    /// the renamed table automatically. Rows are copied before the drop, so the reference stays valid
    /// throughout.
    /// </para>
    /// </summary>
    public const string WidenDismissTriggerAndMetadataKind = """
        CREATE TABLE IF NOT EXISTS System_Notification_New (
            Id                TEXT    NOT NULL PRIMARY KEY,
            Type              TEXT    NOT NULL
                              CHECK (Type IN ('Information', 'Warning', 'Error', 'Success', 'ActionRequired')),
            Body              TEXT    NOT NULL,
            ExpiresAt         TEXT,
            IsDismissed       INTEGER NOT NULL DEFAULT 0,
            DismissedAt       TEXT,
            DismissTriggerKey TEXT
                              CHECK (DismissTriggerKey IS NULL OR DismissTriggerKey IN ('DatabaseReset', 'Reseed')),
            DateCreated       TEXT    NOT NULL,
            DateModified      TEXT,
            DateDeleted       TEXT,
            IsDeleted         INTEGER NOT NULL DEFAULT 0,
            Title             TEXT,
            Metadata          TEXT,
            MetadataKind      TEXT
                              CHECK (MetadataKind IS NULL OR MetadataKind IN ('Announcement', 'SchemaVersionOvershoot', 'WhatsNew', 'ReseedRecommended')),
            AppVersionId      TEXT    REFERENCES System_AppVersion(Id),
            OriginalLanguage  TEXT    NOT NULL DEFAULT 'en'
        );

        INSERT INTO System_Notification_New (
            Id, Type, Body, ExpiresAt, IsDismissed, DismissedAt, DismissTriggerKey,
            DateCreated, DateModified, DateDeleted, IsDeleted,
            Title, Metadata, MetadataKind, AppVersionId, OriginalLanguage)
        SELECT
            Id, Type, Body, ExpiresAt, IsDismissed, DismissedAt, DismissTriggerKey,
            DateCreated, DateModified, DateDeleted, IsDeleted,
            Title, Metadata, MetadataKind, AppVersionId, OriginalLanguage
        FROM System_Notification;

        DROP TABLE System_Notification;

        ALTER TABLE System_Notification_New RENAME TO System_Notification;

        CREATE INDEX IF NOT EXISTS IX_System_Notification_Active ON System_Notification (IsDismissed, IsDeleted, ExpiresAt);
        CREATE INDEX IF NOT EXISTS IX_System_Notification_DismissTriggerKey ON System_Notification (DismissTriggerKey);
        """;
}
