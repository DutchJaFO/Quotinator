namespace Quotinator.Data.Database;

/// <summary>
/// #319's schema for translated notification text: the original's language on the notification row
/// itself, and a sibling table holding one translated title/body per language.
/// <para>
/// Split across two migrations rather than one, per this project's one-schema-change-per-migration
/// rule — a multi-statement migration is harder to reason about when it fails partway through, and
/// these two are independent of each other.
/// </para>
/// <para>
/// The original-language text stays on <c>System_Notification</c> and is never written into
/// <c>System_NotificationTranslation</c>. The read path resolves
/// <c>COALESCE(translation, original)</c>, which only works if the original remains on the parent
/// row — the same arrangement <c>Quotinator_Quote</c>/<c>Quotinator_QuoteTranslation</c> uses.
/// </para>
/// </summary>
internal static class NotificationTranslationMigrations
{
    /// <summary>
    /// Records which language a notification's own <c>Title</c>/<c>Body</c> are written in.
    /// <para>
    /// <c>DEFAULT 'en'</c> backfills every existing row to English, which is a statement of fact
    /// rather than a guess: every notification any released build has written is English. The read
    /// path falls back to this column when a requested language has no translation, so a row left
    /// unpopulated would resolve to nothing at all.
    /// </para>
    /// <para>
    /// No CHECK constraint: ADR 008 governs enum-backed columns, and a language code is not one —
    /// consistent with <c>Quotinator_QuoteTranslation.Language</c>, which has none either.
    /// </para>
    /// </summary>
    internal const string AddOriginalLanguageColumn = """
        ALTER TABLE System_Notification ADD COLUMN OriginalLanguage TEXT NOT NULL DEFAULT 'en';
        """;

    /// <summary>
    /// One translated <c>Title</c>/<c>Body</c> pair per notification per language.
    /// <para>
    /// <c>Title</c> is nullable and independent of <c>Body</c>: a notification may carry a body with
    /// no title, and the read path's <c>COALESCE</c> is per-field, so a translation supplying only a
    /// body falls back to the original title rather than dropping it.
    /// </para>
    /// <para>
    /// <c>UNIQUE (NotificationId, Language)</c> mirrors <c>Quotinator_QuoteTranslation</c>'s own
    /// constraint — one translation per language per notification, enforced by the database rather
    /// than by whichever producer happens to be writing.
    /// </para>
    /// </summary>
    internal const string CreateNotificationTranslationTable = """
        CREATE TABLE IF NOT EXISTS System_NotificationTranslation (
            Id             TEXT    PRIMARY KEY,
            NotificationId TEXT    NOT NULL REFERENCES System_Notification(Id),
            Language       TEXT    NOT NULL,
            Title          TEXT,
            Body           TEXT    NOT NULL,
            DateCreated    TEXT    NOT NULL,
            DateModified   TEXT,
            DateDeleted    TEXT,
            IsDeleted      INTEGER NOT NULL DEFAULT 0,
            UNIQUE (NotificationId, Language)
        );
        """;


    /// <summary>
    /// Gives v1.8.3's shipped operation-id-rename announcement its Dutch and German translations — the
    /// only notification any released build has actually persisted, so this backfills one row rather
    /// than a corpus.
    /// <para>
    /// Identifies the row by <c>MetadataKind</c> plus the payload's own <c>announcement</c> key, read
    /// with <c>json_extract</c>. Matching the whole <c>Metadata</c> string cannot work: migration 11
    /// <c>json_insert</c>s further fields into that column, so the value v1.8.3 wrote is not the value
    /// this migration meets. Reading one key is also stable against any later field being added.
    /// </para>
    /// <para>
    /// Conditional on that notification being present, exactly as migration 9 is, which is what stops
    /// this inventing content: a database created fresh by a later build reaches this migration too
    /// (baseline path, version recorded, then incremental) and never ran v1.8.3, so it matches nothing
    /// and gains nothing. The <c>NOT EXISTS</c> guard makes each language's insert idempotent.
    /// </para>
    /// <para>
    /// The text is a frozen copy of the <c>NotificationOperationIdRename*</c> keys in
    /// <c>i18ntext/UI.*.json</c>. The duplication is deliberate: migration text must not follow a later
    /// edit to those keys.
    /// </para>
    /// </summary>
    internal const string BackfillAnnouncementTranslations = """
        INSERT INTO System_NotificationTranslation (Id, NotificationId, Language, Title, Body, DateCreated, IsDeleted)
        SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' ||
                   lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6))),
               n.Id,
               'nl',
               'Twee API-bewerkings-ID''s zijn hernoemd',
               'Twee REST API-bewerkings-ID''s zijn hernoemd voor consistente naamgeving (issue #279): ' ||
               'GetImportBatches → GetAllImportBatches en GetFileResources → GetAllFileResources. ' ||
               'Dit raakt alleen een gegenereerde API-client die op bewerkings-ID werkt — routes en gedrag zijn ongewijzigd.',
               strftime('%Y-%m-%d %H:%M:%S', 'now'),
               0
        FROM System_Notification n
        WHERE n.MetadataKind = 'Announcement'
          AND n.Metadata IS NOT NULL
          AND json_valid(n.Metadata)
          AND json_extract(n.Metadata, '$.announcement') = 'GetAllImportBatches'
          AND NOT EXISTS (SELECT 1 FROM System_NotificationTranslation t
                          WHERE LOWER(t.NotificationId) = LOWER(n.Id) AND LOWER(t.Language) = LOWER('nl'));

        INSERT INTO System_NotificationTranslation (Id, NotificationId, Language, Title, Body, DateCreated, IsDeleted)
        SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' ||
                   lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6))),
               n.Id,
               'de',
               'Zwei API-Operations-IDs wurden umbenannt',
               'Zwei REST-API-Operations-IDs wurden aus Gründen der Namenskonsistenz umbenannt (Issue #279): ' ||
               'GetImportBatches → GetAllImportBatches und GetFileResources → GetAllFileResources. ' ||
               'Betroffen ist nur ein generierter API-Client, der die Operations-ID verwendet — Routen und Verhalten bleiben unverändert.',
               strftime('%Y-%m-%d %H:%M:%S', 'now'),
               0
        FROM System_Notification n
        WHERE n.MetadataKind = 'Announcement'
          AND n.Metadata IS NOT NULL
          AND json_valid(n.Metadata)
          AND json_extract(n.Metadata, '$.announcement') = 'GetAllImportBatches'
          AND NOT EXISTS (SELECT 1 FROM System_NotificationTranslation t
                          WHERE LOWER(t.NotificationId) = LOWER(n.Id) AND LOWER(t.Language) = LOWER('de'));
        """;
}
