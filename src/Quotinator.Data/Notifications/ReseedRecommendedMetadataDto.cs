using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.ReseedRecommended"/> notification (#304) — why a
/// reseed is being recommended, and for the content-changed case which source files changed.
/// <para>
/// The reason and the changed-file set are also what identify the notification: the same files changing
/// again across a restart is the same unresolved recommendation, while a different set is a genuinely
/// different one and must notify separately. That is why the file names live here rather than only in
/// the body text — identity has to survive a round-trip through the <c>Metadata</c> column, and prose
/// cannot be compared structurally.
/// </para>
/// <para>
/// This is also the payload <c>INotificationActionExecutor.ExecuteAsync</c> receives, which is what
/// makes a future per-file reseed a change to the executor rather than to the contract — #312 added the
/// metadata parameter for exactly this case. Nothing narrows a reseed to one file today:
/// <c>ReseedAsync</c> has no per-file overload.
/// </para>
/// </summary>
public sealed class ReseedRecommendedMetadataDto() : NotificationMetadataDto(NotificationMetadataKind.ReseedRecommended)
{
    /// <summary>Why the reseed is recommended.</summary>
    public required ReseedReason Reason { get; init; }

    /// <summary>
    /// The source files whose content changed upstream, for
    /// <see cref="ReseedReason.ContentChanged"/>. Empty for <see cref="ReseedReason.AfterReset"/>, where
    /// nothing changed upstream — the content is simply gone.
    /// </summary>
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];

    /// <summary>
    /// The reason plus the changed-file set. Joined on a newline rather than compared as a list because
    /// identity is compared as a sequence of scalars by the base type; a separator that cannot occur in
    /// a file name keeps two different sets from colliding.
    /// </summary>
    protected override IEnumerable<object?> IdentityComponents => [Reason, string.Join('\n', ChangedFiles)];
}
