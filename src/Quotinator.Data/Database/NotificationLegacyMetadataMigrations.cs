namespace Quotinator.Data.Database;

/// <summary>
/// One-time backfill giving v1.8.3's already-shipped notification the structured identity #312
/// introduced, so it is recognised rather than re-announced. Consumed by
/// <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns the version number.
/// </summary>
public static class NotificationLegacyMetadataMigrations
{
    /// <summary>
    /// Sets <c>Metadata</c>/<c>MetadataKind</c> — and the <c>Title</c> the row predates — on #279's
    /// announcement, the only notification any released build has ever written.
    /// <para>
    /// Without this, every existing v1.8.3 install gets a duplicate on upgrade. #312 moved a
    /// notification's identity out of its message text and into structured metadata; a row written
    /// before that has no metadata, so it cannot be identified, so #279's producer writes a second
    /// copy on the first startup after the upgrade. Reproduced against a real v1.8.3 database before
    /// this migration was written: two rows, the original carrying the old always-on expiry and the
    /// new one carrying none.
    /// </para>
    /// <para>
    /// <b>Matching on body text is sound here specifically, and is not a licence to do it elsewhere.</b>
    /// This project's whole reason for moving identity off message text is that prose is editable and
    /// substring matches are ambiguous. Neither applies to a migration: the text it matches shipped in
    /// v1.8.3 and can never change retroactively, and migration SQL is itself frozen once applied. The
    /// match is against a fixed historical fact rather than a live value — which is exactly why the
    /// runtime path must never do this.
    /// </para>
    /// <para>
    /// <c>Metadata IS NULL</c> is what keeps this narrow. A row already carrying metadata — including
    /// the duplicate on any machine that ran an intermediate #312 build before this migration existed —
    /// is left untouched, so re-running the chain cannot rewrite a correctly identified row. The
    /// duplicate itself is not removed: it is a real row the operator may have read, and deleting user-
    /// visible history to tidy up a transition is not this migration's business.
    /// </para>
    /// </summary>
    public const string BackfillAnnouncementMetadata = """
        UPDATE System_Notification
        SET Metadata     = '{"announcement":"GetAllImportBatches"}',
            MetadataKind = 'Announcement',
            Title        = COALESCE(Title, 'Two API operation IDs were renamed')
        WHERE Metadata IS NULL
          AND Body LIKE '%GetAllImportBatches%';
        """;
}
