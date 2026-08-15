namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL reshaping <c>System_Notification</c> for #312 — a title/body split, a
/// typed metadata payload, and app-version provenance. Consumed by
/// <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns the version number.
/// </summary>
public static class NotificationSchemaMigrations
{
    /// <summary>
    /// Splits the single <c>Message</c> column into <c>Title</c> + <c>Body</c>, adds the
    /// <c>Metadata</c>/<c>MetadataKind</c> pair, and adds the <c>AppVersionId</c> provenance reference.
    /// <para>
    /// Every statement is an <c>ALTER TABLE</c> — no table rebuild is needed, confirmed against
    /// sqlite.org: <c>RENAME COLUMN</c> is supported natively, and <c>ADD COLUMN</c> explicitly permits
    /// an inline <c>CHECK</c> (see ADR 008, which verified this for exactly this case). SQLite has no
    /// <c>IF NOT EXISTS</c> form for either statement, which is fine — a migration runs once per
    /// database, tracked by <c>System_SchemaVersion</c>, and is never independently re-runnable.
    /// </para>
    /// <para>
    /// Every added column is nullable, deliberately: rows written before this migration have no title,
    /// no metadata, and no known provenance, and inventing values for them would be fabricating history.
    /// <c>AppVersionId</c> additionally *must* be nullable — SQLite requires a column added with a
    /// <c>REFERENCES</c> clause to default to <c>NULL</c>.
    /// </para>
    /// The <c>MetadataKind</c> CHECK is nullable-aware (<c>IS NULL OR IN (...)</c>), matching
    /// <c>DismissTriggerKey</c>'s own existing shape — <see langword="null"/> means "no metadata", which
    /// is why <see cref="Enums.NotificationMetadataKind"/> has no <c>None</c> member.
    /// </summary>
    public const string SplitMessageAndAddMetadata = """
        ALTER TABLE System_Notification RENAME COLUMN Message TO Body;
        ALTER TABLE System_Notification ADD COLUMN Title TEXT;
        ALTER TABLE System_Notification ADD COLUMN Metadata TEXT;
        ALTER TABLE System_Notification ADD COLUMN MetadataKind TEXT
            CHECK (MetadataKind IS NULL OR MetadataKind IN ('Announcement', 'SchemaVersionOvershoot', 'WhatsNew'));
        ALTER TABLE System_Notification ADD COLUMN AppVersionId TEXT REFERENCES System_AppVersion(Id);
        """;
}
