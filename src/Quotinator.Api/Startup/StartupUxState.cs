namespace Quotinator.Api.Startup;

/// <summary>
/// Tracks whether the one-time startup-success popup (#263) has been dismissed yet this process run.
/// Deliberately separate from <see cref="DatabaseHealthState"/> — one tracks health (can flip back
/// after a Reset), the other tracks a one-time notification that never resets during a process run.
/// </summary>
internal sealed class StartupUxState
{
    /// <summary>Whether the startup-success popup has already been dismissed this process run.</summary>
    public bool SummaryDismissed { get; private set; }

    /// <summary>Marks the startup-success popup as dismissed for the rest of this process run.</summary>
    public void Dismiss() => SummaryDismissed = true;
}
