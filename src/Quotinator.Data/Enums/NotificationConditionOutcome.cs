namespace Quotinator.Data.Enums;

/// <summary>What one notification condition check did when it ran (#348).</summary>
public enum NotificationConditionOutcome
{
    /// <summary>The notification was already in the state the condition calls for; nothing changed.</summary>
    Unchanged,

    /// <summary>The condition holds and no notification was open for it, so one was raised.</summary>
    Raised,

    /// <summary>The condition no longer holds, so the open notification was resolved.</summary>
    Cleared,
}
