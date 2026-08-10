namespace Quotinator.Api.Startup;

/// <summary>
/// Tracks whether startup database initialisation has finished running at all — independent of
/// <see cref="DatabaseHealthState"/>, which tracks whether it *succeeded*. Registered as a singleton
/// and set once, at most, from <c>Program.cs</c>'s own top-level startup sequence (#280) — never
/// mutated again afterward. While <see cref="IsComplete"/> is <c>false</c>, Kestrel is listening (the
/// startup sequence now calls <c>StartAsync</c> before running initialisation) but every non-exempt
/// request is served a wait page by <see cref="Middleware.StartupWaitMiddleware"/> instead of reaching
/// routing — even a *failed* initialisation still marks this complete, since a degraded startup has
/// its own existing UI (<see cref="Middleware.DatabaseHealthGateMiddleware"/>/#263's modals), not the
/// wait page.
/// </summary>
internal sealed class StartupPhaseState
{
    /// <summary>False until <see cref="MarkComplete"/> is called.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Records that startup initialisation has finished (successfully or not). Idempotent.</summary>
    public void MarkComplete() => IsComplete = true;
}
