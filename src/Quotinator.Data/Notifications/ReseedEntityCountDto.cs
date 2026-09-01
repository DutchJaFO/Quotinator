using System.Text.Json.Serialization;

namespace Quotinator.Data.Notifications;

/// <summary>
/// How many rows of one entity type a reseeded file added and modified (#302). One entry per type that
/// actually changed — a type with nothing to report is omitted rather than carried as a pair of zeros,
/// matching <see cref="NotificationMetadataDto"/>'s rule that a payload states what it means and omits
/// what it does not.
/// </summary>
/// <remarks>
/// <see cref="EntityType"/> is the same free-text discriminator <c>Import_Action.EntityType</c> carries,
/// so a consumer can line a count up against the actions that produced it. Its values come from
/// <c>ImportActionEntityTypes</c> in <c>Quotinator.Core</c>, which this project cannot reference (ADR
/// 004) — hence a string here rather than an enum, exactly as the <c>Import_Action</c> column itself
/// does it.
/// </remarks>
public sealed class ReseedEntityCountDto
{
    /// <summary>Which entity type these counts describe, e.g. <c>Quote</c> or <c>Source</c>.</summary>
    [JsonPropertyName("entityType")]
    public required string EntityType { get; init; }

    /// <summary>How many rows of this type the file added.</summary>
    [JsonPropertyName("added")]
    public int Added { get; init; }

    /// <summary>How many rows of this type the file modified.</summary>
    [JsonPropertyName("modified")]
    public int Modified { get; init; }
}
