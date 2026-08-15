using System.Text.Json.Serialization;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.WhatsNew"/> notification (#81, restructured by
/// #312) — which release the highlights belong to.
/// <para>
/// <see cref="Version"/> is deliberately distinct from the notification's <c>AppVersionId</c>
/// provenance reference, and the two routinely differ. Provenance records the version that *wrote*
/// the row; this records the version the row is *about*. Upgrading from 1.2 to 1.8.3 writes several
/// what's-new notifications in one startup — every one of them written by 1.8.3, each describing a
/// different release.
/// </para>
/// </summary>
public sealed class WhatsNewMetadataDto() : NotificationMetadataDto(NotificationMetadataKind.WhatsNew)
{
    /// <summary>
    /// The release these highlights describe, or <see langword="null"/> for the <c>unreleased</c>
    /// section — which has no version number yet, and is why this is nullable rather than required.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>
    /// Hash of the highlight text, set only for the <c>unreleased</c> section and <see langword="null"/>
    /// for a tagged release.
    /// <para>
    /// A released version's highlights are frozen, so its version number alone identifies it.
    /// <c>unreleased</c> has no version and its content changes freely during development — identifying
    /// it by content means it re-announces itself when the highlights actually change, and stays quiet
    /// across restarts when they don't.
    /// </para>
    /// </summary>
    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; init; }

    /// <summary>
    /// Version and content hash together. Exactly one is populated in practice, and using both means
    /// the released and unreleased cases need no special-casing at the comparison site.
    /// </summary>
    protected override IEnumerable<object?> IdentityComponents => [Version, ContentHash];
}
