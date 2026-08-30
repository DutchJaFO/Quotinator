using Dapper.Contrib.Extensions;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>
/// A persisted, non-fatal/informational/action-needed message surfaced at startup and reviewable via
/// its own history page and REST endpoints (#278). <see cref="RecordBase.DateCreated"/> is the
/// notification's own created-at timestamp — no separate duplicate column, unlike
/// <see cref="AuditEntryEntity"/>'s own documented <c>PerformedAt</c>/<c>DateCreated</c> redundancy,
/// since nothing here needs a timestamp distinct from creation.
/// </summary>
[Table("System_Notification")]
public sealed class NotificationEntity : RecordBase
{
    /// <summary>Severity/kind of this notification.</summary>
    public SafeValue<NotificationType?> Type { get; init; } = SafeValue<NotificationType?>.Empty;

    /// <summary>
    /// Short headline. <see langword="null"/> when a producer supplies only a body — including every
    /// row written before #312 introduced this column, which is why it is nullable rather than
    /// backfilled with invented text.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The specific message text — carries the concrete reason/recommendation, not the
    /// <see cref="Type"/>. Named <c>Message</c> until #312 split it from <see cref="Title"/>.
    /// </summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>
    /// Free-form, producer-owned JSON payload, or <see langword="null"/> when this notification carries
    /// none. Shape is named by <see cref="MetadataKind"/> — never inferred. One reserved key,
    /// <c>dedupeKey</c>, is read by the shared write-once helper regardless of kind; everything else
    /// belongs to the producing feature, including any parameters its associated action needs.
    /// </summary>
    public string? Metadata { get; init; }

    /// <summary>
    /// Names the shape of <see cref="Metadata"/>. Empty when <see cref="Metadata"/> is
    /// <see langword="null"/> — the absence of metadata is why
    /// <see cref="NotificationMetadataKind"/> deliberately has no <c>None</c> member.
    /// </summary>
    public SafeValue<NotificationMetadataKind?> MetadataKind { get; init; } = SafeValue<NotificationMetadataKind?>.Empty;

    /// <summary>
    /// The <c>System_AppVersion</c> row for the application version that *added* this notification —
    /// provenance, frozen at write time. Distinct from whatever version the notification may be
    /// *about*, which is a producer concern recorded in <see cref="Metadata"/>.
    /// <see langword="null"/> for rows written before #312 introduced this column.
    /// </summary>
    public Guid? AppVersionId { get; init; }

    /// <summary>
    /// When this notification stops being considered active, or <see langword="null"/> when it never
    /// expires. Expiry is always optional (#312): a producer that wants time-limited behaviour asks
    /// for it explicitly, and nothing applies one on its behalf.
    /// </summary>
    public SafeValue<DateTime?> ExpiresAt { get; init; } = SafeValue<DateTime?>.Empty;

    /// <summary>
    /// ISO 639-1 code of the language <see cref="Title"/> and <see cref="Body"/> are written in (#319).
    /// Every row written before that column existed is <c>en</c>, backfilled by migration 12 — a
    /// statement of fact about the shipped corpus, not a guess.
    /// <para>
    /// The read path falls back to this language's text whenever the requested language has no
    /// translation, which is why the original text stays here rather than moving into
    /// <see cref="NotificationTranslationEntity"/> alongside the others.
    /// </para>
    /// </summary>
    public string OriginalLanguage { get; init; } = "en";

    /// <summary>
    /// The language actually resolved for this read — the requested one when a translation existed,
    /// <see cref="OriginalLanguage"/> when it did not. Populated by the read projection's own
    /// <c>CASE</c>, never stored.
    /// <para>
    /// <c>[Computed]</c> so Dapper.Contrib and <c>ReflectedColumnMetadata</c> both exclude it from
    /// writes: it is a property of one query's result, not a column of the table.
    /// </para>
    /// </summary>
    [Computed]
    public string? EffectiveLanguage { get; init; }

    /// <summary>Whether this notification has been dismissed. Mirrors <see cref="RecordBase.IsDeleted"/>/<see cref="RecordBase.DateDeleted"/>'s own flag-plus-timestamp pairing style.</summary>
    public bool IsDismissed { get; set; }

    /// <summary>When this notification was dismissed, or <see langword="null"/> if it hasn't been.</summary>
    public SafeValue<DateTime?> DismissedAt { get; set; } = SafeValue<DateTime?>.Empty;

    /// <summary>
    /// Which action, if performed, supersedes this notification — see
    /// <see cref="Enums.NotificationDismissTrigger"/>. <see langword="null"/> when nothing dismisses
    /// this notification automatically.
    /// </summary>
    public SafeValue<NotificationDismissTrigger?> DismissTriggerKey { get; init; } = SafeValue<NotificationDismissTrigger?>.Empty;

    /// <summary>
    /// Why this notification stopped being active — see <see cref="Enums.NotificationDismissReason"/>.
    /// <see langword="null"/> while it is still active, and on rows dismissed before #304 added this
    /// column, where the reason genuinely is not known rather than being one value or the other.
    /// </summary>
    public SafeValue<NotificationDismissReason?> DismissReason { get; set; } = SafeValue<NotificationDismissReason?>.Empty;
}
