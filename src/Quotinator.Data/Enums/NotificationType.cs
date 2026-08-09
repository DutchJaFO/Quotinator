using Quotinator.Data.Entities;

namespace Quotinator.Data.Enums;

/// <summary>
/// The severity/kind of a <see cref="NotificationEntity"/> (#278). Per ADR 008, backed by a matching
/// SQL CHECK constraint.
/// </summary>
public enum NotificationType
{
    /// <summary>A neutral, non-actionable message.</summary>
    Information,

    /// <summary>A non-fatal condition worth the operator's attention, but not an error.</summary>
    Warning,

    /// <summary>A failure or fault condition.</summary>
    Error,

    /// <summary>A positive confirmation that something completed as expected.</summary>
    Success,

    /// <summary>
    /// Recommends a specific follow-up action (e.g. "consider running a Reset"). The specific reason
    /// lives in the notification's own <see cref="NotificationEntity.Message"/>, not as a separate
    /// enum value per scenario.
    /// </summary>
    ActionRequired
}
