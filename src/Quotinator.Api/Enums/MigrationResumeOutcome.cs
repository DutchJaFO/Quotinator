namespace Quotinator.Api.Enums;

/// <summary>How resuming a migration refused for want of a backup ended (#490 step 17).</summary>
public enum MigrationResumeOutcome
{
    /// <summary>The migration ran and the application is healthy.</summary>
    Resumed,

    /// <summary>No migration was waiting: the application is healthy, or degraded for a reason a migration cannot resolve.</summary>
    NothingToResume,

    /// <summary>The backup still could not be taken, and the operator had not confirmed going without one. Nothing changed.</summary>
    RefusedForBackup,

    /// <summary>Initialisation ran again but degraded for another reason, which replaced the refusal.</summary>
    StillDegraded,
}
