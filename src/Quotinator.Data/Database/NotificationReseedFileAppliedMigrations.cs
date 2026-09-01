namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL widening <c>System_Notification.MetadataKind</c> for #302, which adds
/// <c>ReseedFileApplied</c>. Consumed by <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which
/// assigns the version number.
/// </summary>
public static class NotificationReseedFileAppliedMigrations
{
    /// <summary>
    /// Rebuilds the table with the <c>MetadataKind</c> CHECK widened, per ADR 008's enum-backed-column
    /// checklist.
    /// <para>
    /// A rebuild rather than <c>ALTER TABLE</c>, for the same reason migration 15 needed one: SQLite has
    /// no <c>ALTER TABLE ... MODIFY CHECK</c> (verified against sqlite.org), so widening an existing
    /// CHECK means create-new + copy + drop + rename. Only a *newly added* column may carry its CHECK
    /// inline, which is what let migration 16 avoid a rebuild.
    /// </para>
    /// <para>
    /// Column order reproduces the incremental path's real result, not a tidier one — <c>Title</c>,
    /// <c>Metadata</c>, <c>MetadataKind</c> and <c>AppVersionId</c> trail <c>IsDeleted</c> because
    /// migration 5 appended them, <c>OriginalLanguage</c> trails those because migration 12 did, and
    /// <c>DismissReason</c> trails everything because migration 16 did. The baseline/incremental parity
    /// test compares column ordinals, so any reordering here fails it.
    /// </para>
    /// <para>
    /// The indexes are recreated because <c>DROP TABLE</c> takes them with it. Foreign keys are not
    /// disabled around the rename: <c>System_NotificationTranslation.NotificationId</c> references this
    /// table, and SQLite's default <c>legacy_alter_table=OFF</c> behaviour repoints such references to
    /// the renamed table automatically. Rows are copied before the drop, so the reference stays valid
    /// throughout.
    /// </para>
    /// </summary>
    public const string WidenMetadataKindForFileApplied = """
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
                              CHECK (MetadataKind IS NULL OR MetadataKind IN ('Announcement', 'SchemaVersionOvershoot', 'WhatsNew', 'ReseedRecommended', 'ReseedFileApplied')),
            AppVersionId      TEXT    REFERENCES System_AppVersion(Id),
            OriginalLanguage  TEXT    NOT NULL DEFAULT 'en',
            DismissReason     TEXT
                              CHECK (DismissReason IS NULL OR DismissReason IN ('Dismissed', 'Resolved'))
        );

        INSERT INTO System_Notification_New (
            Id, Type, Body, ExpiresAt, IsDismissed, DismissedAt, DismissTriggerKey,
            DateCreated, DateModified, DateDeleted, IsDeleted,
            Title, Metadata, MetadataKind, AppVersionId, OriginalLanguage, DismissReason)
        SELECT
            Id, Type, Body, ExpiresAt, IsDismissed, DismissedAt, DismissTriggerKey,
            DateCreated, DateModified, DateDeleted, IsDeleted,
            Title, Metadata, MetadataKind, AppVersionId, OriginalLanguage, DismissReason
        FROM System_Notification;

        DROP TABLE System_Notification;

        ALTER TABLE System_Notification_New RENAME TO System_Notification;

        CREATE INDEX IF NOT EXISTS IX_System_Notification_Active ON System_Notification (IsDismissed, IsDeleted, ExpiresAt);
        CREATE INDEX IF NOT EXISTS IX_System_Notification_DismissTriggerKey ON System_Notification (DismissTriggerKey);
        """;
}
