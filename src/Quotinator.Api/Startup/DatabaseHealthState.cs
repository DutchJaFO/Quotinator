using Quotinator.Data.Database.Checks;

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
    /// The check startup ran on the main database (#490, ADR 028), whatever it found, or
    /// <see langword="null"/> when none ran. It names the layer a degraded start failed at, and a finding
    /// that leaves startup healthy, a References failure under decision E, so either is a result a caller
    /// reads rather than a line in a log. A Reset does not replace it: it is what startup found.
    /// </summary>
    public DatabaseCheckResult? StartupCheck { get; private set; }

    /// <summary>
    /// Whether the application is degraded because a migration was refused for want of a backup (#490 step 17),
    /// so <c>POST /api/v1/admin/database/migrate</c> can resume it, with the backup once there is room, or
    /// without one once the operator confirms. Cleared by anything that changes the reason.
    /// </summary>
    public bool MigrationAwaitsConfirmation { get; private set; }

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
    /// Records the check startup ran on the main database (#490). It changes no health: whether the check
    /// degrades startup is decided by the caller, through <see cref="MarkFailed"/>.
    /// </summary>
    /// <param name="check">What the check found.</param>
    public void RecordStartupCheck(DatabaseCheckResult check) => StartupCheck = check;

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
    /// Records that initialisation degraded because a migration was refused for want of a backup (#490 step 17),
    /// which the migrate endpoint can resume. Otherwise as <see cref="MarkFailed"/>.
    /// </summary>
    /// <param name="reason">A short, user-facing description of the refusal and its remedies.</param>
    public void MarkMigrationRefused(string reason)
    {
        // Already degraded for an earlier reason at startup, that reason stands (first wins), and resuming the
        // migration would not resolve it, so nothing awaits the operator's confirmation. Already awaiting it, a
        // second refusal changes nothing.
        if (!IsHealthy)
            return;

        MarkFailed(reason);
        MigrationAwaitsConfirmation = true;
    }

    /// <summary>
    /// Records a failure found by a repair that ran, replacing whatever reason was recorded before it
    /// (#305): a Reset rebuilds every table, so the earlier reason no longer describes the database, and
    /// the application stays degraded throughout rather than passing through healthy.
    /// </summary>
    /// <param name="reason">A short, user-facing description of the failure the repair found.</param>
    public void ReplaceFailure(string reason)
    {
        bool wasHealthy = IsHealthy;
        IsHealthy       = false;
        FailureReason   = reason;
        MigrationAwaitsConfirmation = false;
        if (wasHealthy) Changed?.Invoke();
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
        MigrationAwaitsConfirmation = false;
        if (wasDegraded) Changed?.Invoke();
    }
}
