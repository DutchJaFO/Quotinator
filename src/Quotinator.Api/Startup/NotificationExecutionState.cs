namespace Quotinator.Api.Startup;

/// <summary>
/// #367: which notifications are currently running their action, held for the lifetime of this process.
/// </summary>
/// <remarks>
/// Deliberately in-memory rather than a stored column (developer decision, 2026-09-01). The fact is
/// only true while the process that owns the execution is alive, so it must die with that process — a
/// persisted marker would outlive the run it describes and strand the notification as permanently
/// executing, needing startup cleanup whose correctness cannot be proven without simulating a crash.
/// <para>
/// Process-scoped rather than per-circuit because a second browser session must see the same answer:
/// a per-circuit flag shows the clicking user that something started but closes none of the
/// double-execution half of #367. Correct only while Quotinator is a single process, which it is by
/// design — one container, and the HA supervisor runs single-container add-ons.
/// </para>
/// <para>
/// Follows <see cref="DatabaseHealthState"/>'s precedent: a mutable process-wide state object
/// registered <c>AddSingleton</c> and injected into the pages that need it.
/// </para>
/// </remarks>
internal sealed class NotificationExecutionState
{
    private readonly Lock _gate = new();
    private readonly HashSet<Guid> _executing = [];

    /// <summary>
    /// Admits the caller to run <paramref name="notificationId"/>'s action, or refuses because another
    /// run already holds it. A caller admitted here must call <see cref="End"/> in a <c>finally</c>.
    /// </summary>
    /// <param name="notificationId">The notification whose action is about to run.</param>
    /// <returns><c>true</c> when the caller may proceed; <c>false</c> when a run is already in flight.</returns>
    public bool TryBegin(Guid notificationId)
    {
        lock (_gate)
            return _executing.Add(notificationId);
    }

    /// <summary>Releases <paramref name="notificationId"/> so it can be run again. Safe to call for an id that is not held.</summary>
    /// <param name="notificationId">The notification whose action has finished, successfully or not.</param>
    public void End(Guid notificationId)
    {
        lock (_gate)
            _executing.Remove(notificationId);
    }

    /// <summary>Whether <paramref name="notificationId"/>'s action is running right now.</summary>
    /// <param name="notificationId">The notification to ask about.</param>
    public bool IsExecuting(Guid notificationId)
    {
        lock (_gate)
            return _executing.Contains(notificationId);
    }

    /// <summary>
    /// Runs <paramref name="action"/> for <paramref name="notificationId"/> if nothing else is, and
    /// releases the claim afterwards however it ends.
    /// </summary>
    /// <remarks>
    /// The claim/release pair lives here rather than at each call site so no caller can forget the
    /// <c>finally</c> — a throwing action that skipped the release would strand the notification as
    /// permanently executing for the life of the process, which is the failure mode this whole type
    /// exists to avoid.
    /// </remarks>
    /// <param name="notificationId">The notification whose action is being run.</param>
    /// <param name="action">The work to run exclusively.</param>
    /// <returns><c>true</c> when <paramref name="action"/> ran; <c>false</c> when it was skipped because a run was already in flight.</returns>
    public async Task<bool> RunExclusivelyAsync(Guid notificationId, Func<Task> action)
    {
        if (!TryBegin(notificationId)) return false;
        try
        {
            await action();
            return true;
        }
        finally
        {
            End(notificationId);
        }
    }
}
