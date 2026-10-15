namespace Quotinator.Api.Startup;

/// <summary>
/// Tracks whether startup database initialisation actually succeeded. Registered as a singleton
/// and set once, at most, from <c>Program.cs</c>'s own top-level startup sequence, never mutated
/// again afterward. A failed initialisation no longer stops the process outright (that would also
/// make <c>POST /api/v1/admin/database/reset</c> unreachable, the one endpoint actually capable of
/// resolving the underlying schema/version mismatch): the app still starts and stays reachable for
/// health/version/admin traffic, degrading every other request instead via
/// <c>DatabaseHealthGateMiddleware</c>.
/// </summary>
internal sealed class DatabaseHealthState
{
    /// <summary>True until <see cref="MarkFailed"/> is called; restored by <see cref="MarkHealthy"/> once a subsequent operation (e.g. an admin Reset) has actually repaired the database. This state does not restart on its own just because the underlying file changed.</summary>
    public bool IsHealthy { get; private set; } = true;

    /// <summary>Non-null while <see cref="IsHealthy"/> is false: a short, user-facing (non-technical) description of the failure.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Raised when <see cref="IsHealthy"/> actually changes, so a rendered component can react to a
    /// repair instead of being reloaded into the new state (#350).
    /// </summary>
    /// <remarks>
    /// Repairing the database from a degraded page used to end in a forced page reload, which is the
    /// only way a component outside the one that ran the repair learned the application was healthy
    /// again. Measured 2026-10-14: any full reload of an interactive page tears the Blazor circuit
    /// down without a close handshake and logs four <c>WebSocketException</c>s at <c>Error</c>, so the
    /// successful path wrote four error lines into the log an operator reads to find real faults.
    /// Subscribers re-render themselves instead, and the circuit survives.
    /// <para>
    /// A subscriber must unsubscribe when it is disposed: this state is a singleton and outlives every
    /// component that listens to it.
    /// </para>
    /// </remarks>
    public event Action? Changed;

    /// <summary>
    /// Records that startup database initialisation failed. Idempotent: a second call is a no-op,
    /// keeping the first failure's reason.
    /// </summary>
    /// <remarks>
    /// #350 briefly distinguished which kind of fault this was, so a database that degraded while
    /// still being queryable (a schema version ahead of this build) could keep its diagnostic pages
    /// reading. That was reversed: a schema of unknown shape is untrusted content, and nothing may be
    /// read from it or shown, however well the query would have executed. One flag is the whole
    /// answer, as it was before.
    /// </remarks>
    /// <param name="reason">A short, user-facing description of the failure, shown on <c>/health</c> and on the degraded page.</param>
    public void MarkFailed(string reason)
    {
        if (!IsHealthy) return;
        IsHealthy     = false;
        FailureReason = reason;
        Changed?.Invoke();
    }

    /// <summary>
    /// Clears a previously recorded failure, called after an operation that genuinely repairs the
    /// database (e.g. a successful admin Reset) completes without throwing. Found live, 2026-08-02:
    /// a Reset call that actually fixed the on-disk schema still left every subsequent request
    /// degraded, since <see cref="IsHealthy"/> never reverted to true on its own. The database file
    /// changing underneath this in-memory state is not something this class can observe by itself.
    /// </summary>
    public void MarkHealthy()
    {
        bool wasDegraded = !IsHealthy;
        IsHealthy        = true;
        FailureReason    = null;
        if (wasDegraded) Changed?.Invoke();
    }
}
