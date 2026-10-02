namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL giving #279's operation-id-rename announcement its line breaks and
/// removing the expiry a v1.8.3 database gave it (#413). Consumed by
/// <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns the version number.
/// </summary>
public static class NotificationAnnouncementLineBreakMigrations
{
    /// <summary>
    /// Clears the announcement's <c>ExpiresAt</c> and restates its body, content hash and Dutch and
    /// German translations over four lines.
    /// <para>
    /// The expiry is the defect: v1.8.3 gave every notification it wrote a 30-day default, so a
    /// database it wrote shows a breaking change that still applies as <c>Expired</c>. #312 removed the
    /// default, and a fresh install already writes no expiry, so only a row v1.8.3 left behind needs
    /// this.
    /// </para>
    /// <para>
    /// This continues what migration 24 began rather than repeating it: that migration reworded the
    /// same row to the producer's then-current single-line text, and this one restates that text over
    /// four lines. Both are in-place edits of a stored notification, which the migration-11 rule
    /// otherwise forbids, because the announcement is being restated rather than replaced by news the
    /// operator has not read. A third such edit needs its own decision, on its own merits.
    /// </para>
    /// <para>
    /// The lines are joined with <c>char(10)</c> rather than written as a literal spanning several
    /// lines, so that C#'s raw-string margin cannot indent the stored text. The hash is a literal
    /// because SQLite has no SHA-256; <c>OperationIdRenameAnnouncementTests</c> applies this migration
    /// and compares what it stored against what the producer writes, so the literal cannot drift
    /// unnoticed.
    /// </para>
    /// <para>
    /// <c>IsDismissed</c> is untouched: a dismissed announcement stays dismissed. Identified by the
    /// payload's own <c>announcement</c> key, as migrations 14 and 24 are, so a database that never
    /// held the announcement matches nothing and a replay assigns the same values again.
    /// </para>
    /// </summary>
    public const string BreakAnnouncementIntoLines = """
        UPDATE System_Notification
        SET Body      = 'Two REST API operation IDs were renamed for naming consistency (issue #279):' || char(10) ||
                        'GetImportBatches → GetAllImportBatches' || char(10) ||
                        'GetFileResources → GetAllFileResources' || char(10) ||
                        'This only affects a generated API client keyed by operation ID; routes and behaviour are unchanged.',
            ExpiresAt = NULL,
            Metadata  = json_set(Metadata, '$.contentHash', '9B321D33')
        WHERE MetadataKind = 'Announcement'
          AND Metadata IS NOT NULL
          AND json_valid(Metadata)
          AND json_extract(Metadata, '$.announcement') = 'GetAllImportBatches';

        UPDATE System_NotificationTranslation
        SET Body = 'Twee REST API-bewerkings-ID''s zijn hernoemd voor consistente naamgeving (issue #279):' || char(10) ||
                   'GetImportBatches → GetAllImportBatches' || char(10) ||
                   'GetFileResources → GetAllFileResources' || char(10) ||
                   'Dit raakt alleen een gegenereerde API-client die op bewerkings-ID werkt; routes en gedrag zijn ongewijzigd.'
        WHERE LOWER(Language) = LOWER('nl')
          AND LOWER(NotificationId) IN (SELECT LOWER(Id) FROM System_Notification
                                 WHERE MetadataKind = 'Announcement'
                                   AND json_valid(Metadata)
                                   AND json_extract(Metadata, '$.announcement') = 'GetAllImportBatches');

        UPDATE System_NotificationTranslation
        SET Body = 'Zwei REST-API-Operations-IDs wurden aus Gründen der Namenskonsistenz umbenannt (Issue #279):' || char(10) ||
                   'GetImportBatches → GetAllImportBatches' || char(10) ||
                   'GetFileResources → GetAllFileResources' || char(10) ||
                   'Betroffen ist nur ein generierter API-Client, der die Operations-ID verwendet; Routen und Verhalten bleiben unverändert.'
        WHERE LOWER(Language) = LOWER('de')
          AND LOWER(NotificationId) IN (SELECT LOWER(Id) FROM System_Notification
                                 WHERE MetadataKind = 'Announcement'
                                   AND json_valid(Metadata)
                                   AND json_extract(Metadata, '$.announcement') = 'GetAllImportBatches');
        """;
}
