using System.Text.Json.Serialization;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.DatabaseCreated"/> notification (#371): the two
/// schema versions the database was created at.
/// <para>
/// The versions are here rather than only in the message text for the same reason
/// <see cref="SchemaVersionOvershootMetadataDto"/>'s are: message text is localisable prose that can
/// be rewritten at any time, so anything a consumer needs to act on belongs in structured metadata.
/// </para>
/// </summary>
public sealed class DatabaseCreatedMetadataDto()
    : NotificationMetadataDto(NotificationMetadataKind.DatabaseCreated)
{
    /// <summary>The version of Quotinator.Data's own migration list the database was created at.</summary>
    [JsonPropertyName("dataSchemaVersion")]
    public required int DataSchemaVersion { get; init; }

    /// <summary>The version of the consuming project's own migration list the database was created at.</summary>
    [JsonPropertyName("appSchemaVersion")]
    public required int AppSchemaVersion { get; init; }

    /// <summary>
    /// The two versions together, matching <see cref="SchemaVersionOvershootMetadataDto"/>'s own
    /// answer to the same question.
    /// </summary>
    /// <remarks>
    /// Creation can only happen once per database, since the database this row lives in did not exist
    /// before it, so identity has no duplicate to suppress in practice. The versions are still what
    /// identifies it: a notification whose identity is its kind alone would make the single-row
    /// property an accident of the producer rather than something stated.
    /// </remarks>
    protected override IEnumerable<object?> IdentityComponents => [DataSchemaVersion, AppSchemaVersion];
}
