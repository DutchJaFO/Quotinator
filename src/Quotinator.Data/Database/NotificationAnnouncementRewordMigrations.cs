namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL rewording #279's operation-id-rename announcement (#348). Consumed by
/// <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns the version number.
/// </summary>
public static class NotificationAnnouncementRewordMigrations
{
    /// <summary>
    /// Rewrites the stored announcement, its content hash and its Dutch and German translations to the
    /// producer's current text.
    /// <para>
    /// The content hash is part of the announcement's identity, so without this the reworded producer
    /// would not recognise the row it wrote before and would announce the same news a second time on
    /// every installation. The hash is written as a literal because SQLite has no SHA-256; the Api's
    /// <c>OperationIdRenameAnnouncementTests</c> hold it, and every text here, to what the producer writes.
    /// </para>
    /// <para>
    /// Identified by the payload's own <c>announcement</c> key, as migration 14 does. Setting the same
    /// values again changes nothing, so a replay is harmless, and a database that never held the
    /// announcement matches nothing.
    /// </para>
    /// </summary>
    public const string RewordOperationIdRename = """
        UPDATE System_Notification
        SET Body     = 'Two REST API operation IDs were renamed for naming consistency (issue #279): GetImportBatches → GetAllImportBatches, and GetFileResources → GetAllFileResources. This only affects a generated API client keyed by operation ID; routes and behaviour are unchanged.',
            Metadata = json_set(Metadata, '$.contentHash', '6FC95BB0')
        WHERE MetadataKind = 'Announcement'
          AND Metadata IS NOT NULL
          AND json_valid(Metadata)
          AND json_extract(Metadata, '$.announcement') = 'GetAllImportBatches';

        UPDATE System_NotificationTranslation
        SET Body = 'Twee REST API-bewerkings-ID''s zijn hernoemd voor consistente naamgeving (issue #279): GetImportBatches → GetAllImportBatches en GetFileResources → GetAllFileResources. Dit raakt alleen een gegenereerde API-client die op bewerkings-ID werkt; routes en gedrag zijn ongewijzigd.'
        WHERE LOWER(Language) = LOWER('nl')
          AND LOWER(NotificationId) IN (SELECT LOWER(Id) FROM System_Notification
                                 WHERE MetadataKind = 'Announcement'
                                   AND json_valid(Metadata)
                                   AND json_extract(Metadata, '$.announcement') = 'GetAllImportBatches');

        UPDATE System_NotificationTranslation
        SET Body = 'Zwei REST-API-Operations-IDs wurden aus Gründen der Namenskonsistenz umbenannt (Issue #279): GetImportBatches → GetAllImportBatches und GetFileResources → GetAllFileResources. Betroffen ist nur ein generierter API-Client, der die Operations-ID verwendet; Routen und Verhalten bleiben unverändert.'
        WHERE LOWER(Language) = LOWER('de')
          AND LOWER(NotificationId) IN (SELECT LOWER(Id) FROM System_Notification
                                 WHERE MetadataKind = 'Announcement'
                                   AND json_valid(Metadata)
                                   AND json_extract(Metadata, '$.announcement') = 'GetAllImportBatches');
        """;
}