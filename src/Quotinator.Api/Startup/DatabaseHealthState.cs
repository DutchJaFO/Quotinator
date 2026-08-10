namespace Quotinator.Api.Startup;

/// <summary>
/// Tracks whether startup database initialisation actually succeeded. Registered as a singleton
/// and set once, at most, from <c>Program.cs</c>'s own top-level startup sequence — never mutated
/// again afterward. A failed initialisation no longer stops the process outright (that would also
/// make <c>POST /api/v1/admin/database/reset</c> unreachable, the one endpoint actually capable of
/// resolving the underlying schema/version mismatch): the app still starts and stays reachable for
/// health/version/admin traffic, degrading every other request instead via
/// <c>DatabaseHealthGateMiddleware</c>.
/// </summary>
internal sealed class DatabaseHealthState
{
    /// <summary>True until <see cref="MarkFailed"/> is called; restored by <see cref="MarkHealthy"/> once a subsequent operation (e.g. an admin Reset) has actually repaired the database — this state does not restart on its own just because the underlying file changed.</summary>
    public bool IsHealthy { get; private set; } = true;

    /// <summary>Non-null while <see cref="IsHealthy"/> is false — a short, user-facing (non-technical) description of the failure.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Records that startup database initialisation failed. Idempotent — a second call is a no-op, keeping the first failure's reason.</summary>
    public void MarkFailed(string reason)
    {
        if (!IsHealthy) return;
        IsHealthy     = false;
        FailureReason = reason;
    }

    /// <summary>
    /// Clears a previously recorded failure — called after an operation that genuinely repairs the
    /// database (e.g. a successful admin Reset) completes without throwing. Found live, 2026-08-02:
    /// a Reset call that actually fixed the on-disk schema still left every subsequent request
    /// degraded, since <see cref="IsHealthy"/> never reverted to true on its own — the database file
    /// changing underneath this in-memory state is not something this class can observe by itself.
    /// </summary>
    public void MarkHealthy()
    {
        IsHealthy     = true;
        FailureReason = null;
    }
}
