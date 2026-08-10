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

    /// <summary>The specific message text.</summary>
    public required string Message { get; init; }

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
