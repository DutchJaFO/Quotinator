using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.WhatsNew"/> notification (#81, restructured by
/// #312) — which release the highlights belong to.
/// <para>
/// It adds no fields of its own: release state, version and content hash are common to every payload,
/// and they are exactly what a what's-new entry is identified by. A tagged release states
/// <see cref="NotificationReleaseState.Released"/> and its version — its highlights are frozen once
/// tagged, so the version alone identifies it. The <c>unreleased</c> section states
/// <see cref="NotificationReleaseState.Unreleased"/> and a content hash instead, because it has no
/// version and its content changes freely during development: identifying it by content means it
/// re-announces when the highlights actually change, and stays quiet across restarts when they don't.
/// </para>
/// </summary>
public sealed class WhatsNewMetadataDto() : NotificationMetadataDto(NotificationMetadataKind.WhatsNew)
{
    /// <summary>
    /// Nothing beyond the common fields. Kept explicit rather than inherited-and-forgotten: the base
    /// declares this abstract precisely so a payload cannot exist without answering the question, and
    /// "the common fields already say everything" is a real answer.
    /// </summary>
    protected override IEnumerable<object?> IdentityComponents => [];
}
