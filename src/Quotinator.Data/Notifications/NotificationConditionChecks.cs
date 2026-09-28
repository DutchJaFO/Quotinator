using Microsoft.Extensions.Logging;
using Quotinator.Data.Logging;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Runs every registered <see cref="INotificationConditionCheck"/> (#348): the one entry point every caller
/// uses, so a kind added later is re-checked everywhere the moment it is registered.
/// </summary>
/// <param name="checks">Every registered condition check.</param>
/// <param name="logger">Reports a check that could not run, for the callers that carry on regardless.</param>
public sealed class NotificationConditionChecks(
    IEnumerable<INotificationConditionCheck> checks,
    ILogger<NotificationConditionChecks> logger)
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

    /// <summary>
    /// Runs every registered check, reporting a failure rather than propagating it (#348).
    /// <para>
    /// For the callers where re-verification follows something else that has already happened: a completed
    /// startup, a Reset, a backup taken or removed, a notification action. There the checks are not what was
    /// asked for, so a check that cannot run must not change the answer the caller gives. Found by
    /// <c>backup/05</c> step 7, where a read-only data directory left the checks unable to write their own
    /// notification and a handled <c>409</c> became an unhandled <c>500</c>.
    /// </para>
    /// <para>
    /// <see cref="RunAsync"/> stays strict, for <c>POST /notifications/refresh</c>, where the checks are the
    /// request: a caller who asked for a refresh and did not get one should be told.
    /// </para>
    /// </summary>
    public async Task RunReportingFailuresAsync()
    {
        try
        {
            await RunAsync();
        }
        catch (Exception ex)
        {
            logger.LogNotificationConditionChecksFailed(ex);
        }
    }
}
