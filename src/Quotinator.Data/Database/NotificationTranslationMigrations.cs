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
}
