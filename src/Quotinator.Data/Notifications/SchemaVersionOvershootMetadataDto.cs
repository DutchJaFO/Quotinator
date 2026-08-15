using System.Text.Json.Serialization;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.SchemaVersionOvershoot"/> notification (#289,
/// restructured by #312) — the two recorded schema versions found to be ahead of what this build
/// expects.
/// <para>
/// These numbers were previously readable only by parsing them back out of the human-readable message
/// text, which is precisely the "trickery with the content itself" #312 exists to remove: message text
/// is localisable prose that can be rewritten at any time, so anything a consumer needs to *act* on
/// belongs in structured metadata instead. A UI offering "reset now" reads the versions from here
/// rather than pattern-matching a sentence.
/// </para>
/// </summary>
public sealed class SchemaVersionOvershootMetadataDto()
    : NotificationMetadataDto(NotificationMetadataKind.SchemaVersionOvershoot)
{
    /// <summary>The recorded version of Quotinator.Data's own migration history.</summary>
    [JsonPropertyName("dataSchemaVersion")]
    public required int DataSchemaVersion { get; init; }

    /// <summary>The recorded version of the consuming project's own migration history.</summary>
    [JsonPropertyName("appSchemaVersion")]
    public required int AppSchemaVersion { get; init; }

    /// <summary>
    /// Both versions together. A repeat of the same already-notified overshoot (the operator hasn't
    /// reset yet and the app restarted) stays deduped, while a genuinely different future overshoot —
    /// a later squash producing different numbers — is correctly a new notification.
    /// </summary>
    protected override IEnumerable<object?> IdentityComponents => [DataSchemaVersion, AppSchemaVersion];
}
