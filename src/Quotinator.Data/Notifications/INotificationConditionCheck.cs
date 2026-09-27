using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Re-checks one kind of notification whose condition can change while the application runs (#348), and
/// raises or resolves it to match.
/// <para>
/// A kind joins by implementing this and registering it once; <see cref="NotificationConditionChecks"/>
/// runs every registration, from every place that asks (a completed startup, the paths that change what the
/// condition depends on, and <c>POST /api/v1/notifications/refresh</c>), so none of them needs code of its
/// own for any kind.
/// </para>
/// </summary>
public interface INotificationConditionCheck
{
    /// <summary>The kind of notification this check raises and resolves.</summary>
    NotificationMetadataKind Kind { get; }

    /// <summary>Reads the condition now, and raises or resolves the notification to match it.</summary>
    /// <returns>What changed, if anything.</returns>
    Task<NotificationConditionOutcome> CheckAsync();
}
