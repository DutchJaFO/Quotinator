using Quotinator.Api.Enums;

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

    /// <summary>Which kind of fault is recorded, null while <see cref="IsHealthy"/> is true (#350).</summary>
    public DatabaseFaultKind? Fault { get; private set; }

    /// <summary>
    /// Whether a live query written against this build's schema can be expected to execute, which is a
    /// different question from <see cref="IsHealthy"/> and the one the diagnostic surfaces ask (#350).
    /// <para>
    /// True while healthy, and false only for <see cref="DatabaseFaultKind.ExpectedSchemaMissing"/>.
    /// A schema-version overshoot holds everything this build expects and more, so the notification
    /// explaining the degraded state, the staged import actions and the file history can all still be
    /// read and shown. What they show is diagnostic, never a claim that the content is authoritative:
    /// quote content stays gated by <c>DatabaseHealthGateMiddleware</c>, which answers from
    /// <see cref="IsHealthy"/>.
    /// </para>
    /// </summary>
    public bool ExpectedSchemaIsPresent => Fault != DatabaseFaultKind.ExpectedSchemaMissing;

    /// <summary>Records that startup database initialisation failed. Idempotent: a second call is a no-op, keeping the first failure's reason and kind.</summary>
    /// <param name="reason">A short, user-facing description of the failure, shown on <c>/health</c> and on the degraded page.</param>
    /// <param name="kind">Which kind of fault this is, and therefore whether a live read may still be attempted. Required so a fault added later cannot inherit an answer by default.</param>
    public void MarkFailed(string reason, DatabaseFaultKind kind)
    {
        if (!IsHealthy) return;
        IsHealthy     = false;
        FailureReason = reason;
        Fault         = kind;
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
        IsHealthy     = true;
        FailureReason = null;
        Fault         = null;
    }
}
