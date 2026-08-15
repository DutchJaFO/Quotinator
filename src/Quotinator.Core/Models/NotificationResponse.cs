namespace Quotinator.Core.Models;

/// <summary>
/// Response shape for <c>GET /api/v1/notifications</c> (list) and the entity returned by
/// <c>POST /api/v1/notifications/{id}/dismiss</c> — #278.
/// </summary>
public sealed class NotificationResponse
{
    /// <summary>Canonical (lowercase) id.</summary>
    public required string Id { get; init; }

    /// <summary>Severity/kind: <c>information</c>, <c>warning</c>, <c>error</c>, <c>success</c>, or <c>actionrequired</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Optional short headline shown above <see cref="Body"/>, or <see langword="null"/> when the producer supplied only a body.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// The specific message text. Named <c>message</c> in responses until #312 split it from
    /// <see cref="Title"/> — a breaking change for any client reading the old field name.
    /// </summary>
    public required string Body { get; init; }

    /// <summary>Free-form producer-owned JSON payload, or <see langword="null"/> when this notification carries none. Shape is named by <see cref="MetadataKind"/>.</summary>
    public string? Metadata { get; init; }

    /// <summary>Names the shape of <see cref="Metadata"/> (e.g. <c>whatsnew</c>), or <see langword="null"/> when there is no metadata.</summary>
    public string? MetadataKind { get; init; }

    /// <summary>UTC timestamp when this notification was created.</summary>
    public DateTime? CreatedAt { get; init; }

    /// <summary>UTC timestamp after which this notification is no longer considered active.</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>Whether this notification has been dismissed.</summary>
    public bool IsDismissed { get; init; }

    /// <summary>UTC timestamp when this notification was dismissed, or <see langword="null"/> if it hasn't been.</summary>
    public DateTime? DismissedAt { get; init; }

    /// <summary>Which action, if performed, supersedes this notification (e.g. <c>databasereset</c>), or <see langword="null"/>.</summary>
    public string? DismissTriggerKey { get; init; }
}
