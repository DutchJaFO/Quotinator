using System.Text.Json.Serialization;

namespace Quotinator.Data.Notifications;

/// <summary>
/// How many of a reseeded file's import actions are in one reviewable state (#303). One entry per state
/// that actually has rows — a state with none is omitted rather than carried as a zero, matching
/// <see cref="NotificationMetadataDto"/>'s rule that a payload states what it means.
/// </summary>
/// <remarks>
/// <see cref="Status"/> holds an <c>ImportActionStatus</c> name. It is a string rather than the enum
/// because that enum describes an action's own lifecycle and carries states no review can be in
/// (<c>Decided</c>, <c>Applied</c>, <c>Discarded</c>); only the reviewable subset ever appears here, and
/// a payload that could express the others would invite a reader to expect them.
/// </remarks>
public sealed class ImportReviewCountDto
{
    /// <summary>Which reviewable state these actions are in — <c>Pending</c>, <c>Blocked</c> or <c>Stale</c>.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>How many of the file's actions are in that state.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }
}
