using Quotinator.Data.Entities;

namespace Quotinator.Data.Enums;

/// <summary>
/// Names the shape of a <see cref="NotificationEntity"/>'s <c>Metadata</c> payload, so a consumer can
/// deserialize it against a known contract instead of inferring one (#312).
/// <para>
/// Deliberately independent of <see cref="NotificationType"/>: that describes *severity*
/// (Information/Warning/Error/Success/ActionRequired), this describes *payload shape*. The same shape
/// can be surfaced at different severities, and one severity can carry several shapes — conflating
/// them would force a new <see cref="NotificationType"/> member every time a producer needed a new
/// payload.
/// </para>
/// Per ADR 008, backed by a matching (nullable-aware) SQL CHECK constraint. New members are added here
/// — plus their own migration extending the CHECK — only as concrete producers need them, not
/// speculatively; SQLite cannot widen an existing CHECK in place, so each addition costs a table
/// rebuild. <see langword="null"/> on the column means the notification carries no metadata at all,
/// which is why no <c>None</c> member exists here.
/// </summary>
public enum NotificationMetadataKind
{
    /// <summary>
    /// A one-off product announcement whose payload carries only the reserved <c>dedupeKey</c> — no
    /// other structured data. #279's operationId-rename notice is the current example.
    /// </summary>
    Announcement,

    /// <summary>
    /// A schema-version overshoot (#289) — payload carries the recorded data and app schema versions
    /// alongside the reserved <c>dedupeKey</c>.
    /// </summary>
    SchemaVersionOvershoot,

    /// <summary>
    /// A what's-new-after-upgrade notice (#81) — payload identifies which changelog entry the
    /// notification is *about* (a released version, or the unreleased section), distinct from the
    /// provenance recorded by the <c>AppVersionId</c> column, which is always the version that wrote
    /// the row.
    /// </summary>
    WhatsNew,

    /// <summary>
    /// A recommendation to reseed (#304) — the payload carries why, and for the content-changed case
    /// which source files changed. Those file names are identifiers the renderer and the action consume,
    /// not prose: a notification's own text lives in its Title/Body columns, per
    /// <see cref="Notifications.NotificationMetadataDto"/>'s no-text rule.
    /// </summary>
    ReseedRecommended,

    /// <summary>
    /// A confirmation that one source file reseeded with nothing left to review (#302) — the payload
    /// carries the file name and how many rows of each entity type it added or modified.
    /// <para>
    /// Distinct from <see cref="ReseedRecommended"/>, which asks for a reseed that has not happened:
    /// this reports one that has, per file. The breakdown is what identifies it, so the same file
    /// producing a different result notifies separately rather than being suppressed as a duplicate.
    /// </para>
    /// </summary>
    ReseedFileApplied,

    /// <summary>
    /// One reseeded file left import actions awaiting review (#303) — the payload carries the file, the
    /// batch those actions belong to, and how many are in each reviewable state.
    /// <para>
    /// The counterpart to <see cref="ReseedFileApplied"/>: same seeding loop, opposite outcome. The
    /// batch id is part of what identifies it, because the batch *is* the set of reviews being
    /// reported — a later reseed stages a different batch, which is a different thing to review.
    /// </para>
    /// </summary>
    ImportReviewPending
}
