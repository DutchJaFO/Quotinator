namespace Quotinator.Data.Database;

/// <summary>
/// One-time backfills repairing notification rows written before #312's shape existed, so each is
/// recognised rather than re-announced: v1.8.3's already-shipped notification gains the structured
/// identity and provenance it predates, and what's-new rows from intermediate builds gain their explicit
/// release state. Consumed by <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns the
/// version numbers.
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

    /// <summary>
    /// Gives that same notification its <c>AppVersionId</c> provenance, and creates the
    /// <c>System_AppVersion</c> row it points at.
    /// <para>
    /// <see cref="BackfillAnnouncementMetadata"/> restored the row's identity but left its provenance
    /// null, so the stored rows disagreed with each other: a what's-new entry knew which version wrote
    /// it, the v1.8.3-era announcement did not. A separate migration rather than an edit to that one —
    /// migration 8 has already been applied to a real database, and an applied migration is frozen.
    /// </para>
    /// <para>
    /// <b>The writing version is knowable, not a guess.</b> v1.8.3 is the last official release and the
    /// only version that could have written this row: <c>AppVersionId</c> did not exist before #312, so
    /// an announcement carrying #279's payload with no provenance is by construction one written before
    /// that column did.
    /// </para>
    /// <para>
    /// <b>Both statements are conditional on that row being present, which is what stops this inventing
    /// history.</b> A database created fresh by an intermediate #312 build reaches this migration too —
    /// it took the baseline path, recorded its version, and now upgrades incrementally — and it never
    /// ran v1.8.3. A genuinely *fresh* database is excluded by the same design from the other direction:
    /// an empty database takes the one-step baseline path and never replays migrations at all, so a
    /// migration is upgrade-only by construction. That is load-bearing here and invisible from the SQL,
    /// which is why it is stated rather than left to be rediscovered.
    /// </para>
    /// <para>
    /// The sequence number goes *below* the existing minimum rather than above the maximum.
    /// <c>System_AppVersion</c> did not exist in v1.8.3 (#81 introduced it, unreleased), so every row
    /// this table can already hold was written by a later build. Appending would make "the version that
    /// ran last" answer 1.8.3 on a machine that has since run newer builds, and #81's catch-up range
    /// would replay releases it has already announced. On the ordinary v1.8.3 upgrade path the table is
    /// empty, the row lands at 1, and that database gains a correct last-active version for free.
    /// </para>
    /// </summary>
    public const string BackfillAnnouncementProvenance = """
        INSERT INTO System_AppVersion (Id, Application, Version, DateCreated, IsDeleted, SequenceNumber)
        SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' ||
                   lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6))),
               'Quotinator.Api',
               '1.8.3',
               strftime('%Y-%m-%d %H:%M:%S', 'now'),
               0,
               COALESCE((SELECT MIN(SequenceNumber) FROM System_AppVersion), 2) - 1
        WHERE EXISTS (SELECT 1 FROM System_Notification
                      WHERE AppVersionId IS NULL
                        AND Metadata = '{"announcement":"GetAllImportBatches"}')
          AND NOT EXISTS (SELECT 1 FROM System_AppVersion
                          WHERE LOWER(Application) = LOWER('Quotinator.Api') AND Version = '1.8.3');

        UPDATE System_Notification
        SET AppVersionId = (SELECT Id FROM System_AppVersion
                            WHERE LOWER(Application) = LOWER('Quotinator.Api') AND Version = '1.8.3'
                            ORDER BY SequenceNumber LIMIT 1)
        WHERE AppVersionId IS NULL
          AND Metadata = '{"announcement":"GetAllImportBatches"}'
          AND EXISTS (SELECT 1 FROM System_AppVersion
                      WHERE LOWER(Application) = LOWER('Quotinator.Api') AND Version = '1.8.3');
        """;

    /// <summary>
    /// Gives what's-new rows written by an intermediate #312 build the explicit release state that
    /// <c>WhatsNewMetadataDto</c> now requires.
    /// <para>
    /// Those rows told the two cases apart by <c>version</c> being absent. Making the state a
    /// <c>required</c> property means such a row can no longer be deserialized, so it cannot be
    /// identified, so it re-announces itself on the first startup after the upgrade — the same failure
    /// mode <see cref="BackfillAnnouncementMetadata"/> exists to prevent, one shape further on. Only
    /// databases carrying rows from an unreleased build are affected, since #81 has never shipped; that
    /// is a reason to fix it, not a reason to assume nobody has one.
    /// </para>
    /// <para>
    /// The state is derived from the very convention that wrote these rows — a <c>version</c> key meant
    /// a tagged release, its absence meant the unreleased section — which is a fixed historical fact
    /// about already-written rows, exactly as migration 8's body-text match is. <c>json_insert</c> adds
    /// the key only where it is missing, so a row that already states its own release state is left
    /// alone even if this ran twice.
    /// </para>
    /// </summary>
    public const string BackfillWhatsNewReleaseState = """
        UPDATE System_Notification
        SET Metadata = json_insert(Metadata, '$.releaseState',
                CASE WHEN json_extract(Metadata, '$.version') IS NULL THEN 'Unreleased' ELSE 'Released' END)
        WHERE MetadataKind = 'WhatsNew'
          AND Metadata IS NOT NULL
          AND json_valid(Metadata);
        """;

    /// <summary>
    /// Brings every remaining payload shape onto the common release fields — release state, the version
    /// it is about, and its content hash — which stopped being what's-new-specific after the developer
    /// read the stored rows and found the two kinds disagreeing about what a payload states.
    /// <para>
    /// The announcement's values are a fixed historical fact, not a guess: v1.8.3 shipped the operation-id
    /// renames, so that is the release it is about, and its body text shipped with that release and
    /// cannot change retroactively — which is what makes hashing it here safe, and why the hash is a
    /// literal rather than computed (SQLite has no hashing function, and migration SQL is frozen
    /// regardless). If the producer's wording is ever edited, the hashes stop matching and the
    /// notification is re-announced, which is exactly what a content hash is for.
    /// </para>
    /// <para>
    /// A schema-version overshoot is not about a release at all, so it states that outright. Borrowing
    /// the running version instead would make the same unresolved overshoot re-announce itself on every
    /// upgrade, since the version would be part of its identity.
    /// </para>
    /// <para>
    /// Scoped by kind rather than by payload content: a row written after this change already carries a
    /// release state (the property is <c>required</c>), so it can never match
    /// <c>releaseState IS NULL</c> and needs no narrower filter to protect it. <c>json_insert</c> adds
    /// only missing keys, so replaying this cannot rewrite a row that states its own values.
    /// </para>
    /// </summary>
    public const string BackfillCommonReleaseFields = """
        UPDATE System_Notification
        SET Metadata = json_insert(Metadata,
                '$.releaseState', 'Released',
                '$.version',      '1.8.3',
                '$.contentHash',  'E55328BB')
        WHERE MetadataKind = 'Announcement'
          AND Metadata IS NOT NULL
          AND json_valid(Metadata)
          AND json_extract(Metadata, '$.releaseState') IS NULL;

        UPDATE System_Notification
        SET Metadata = json_insert(Metadata, '$.releaseState', 'NotApplicable')
        WHERE MetadataKind = 'SchemaVersionOvershoot'
          AND Metadata IS NOT NULL
          AND json_valid(Metadata)
          AND json_extract(Metadata, '$.releaseState') IS NULL;
        """;
}
