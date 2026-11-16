using Quotinator.Api.Enums;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Startup;

/// <summary>
/// What resuming a migration refused for want of a backup did (#490 step 17), which the migrate endpoint maps to an
/// HTTP response and the degraded page's repair control to a sentence. A refusal is an outcome, not an exception.
/// </summary>
public sealed class MigrationResumeResult
{
    /// <summary>How it ended.</summary>
    public required MigrationResumeOutcome Outcome { get; init; }

    /// <summary>What still stopped the backup, when that is why nothing changed.</summary>
    public BackupOutcome? BackupObstacle { get; init; }

    /// <summary>Why the application is degraded, when it was not resumable, or is still degraded after the attempt.</summary>
    public string? FailureReason { get; init; }

    /// <summary>What the migration left to restore from, when it ran: the backup it took, or that it skipped one, and why.</summary>
    public RestorePointResult? RestorePoint { get; init; }
}
