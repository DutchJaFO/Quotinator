namespace Quotinator.Data.Notifications;

/// <summary>
/// Runs every registered <see cref="INotificationConditionCheck"/> (#348): the one entry point every caller
/// uses, so a kind added later is re-checked everywhere the moment it is registered.
/// </summary>
/// <param name="checks">Every registered condition check.</param>
public sealed class NotificationConditionChecks(IEnumerable<INotificationConditionCheck> checks)
{
    /// <summary>Runs every registered check, one after another.</summary>
    /// <returns>What each check did, in registration order.</returns>
    public async Task<IReadOnlyList<NotificationConditionCheckResult>> RunAsync()
    {
        List<NotificationConditionCheckResult> results = [];
        foreach (INotificationConditionCheck check in checks)
            results.Add(new NotificationConditionCheckResult(check.Kind, await check.CheckAsync()));

        return results;
    }
}
