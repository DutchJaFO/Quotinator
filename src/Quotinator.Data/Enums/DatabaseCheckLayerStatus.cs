namespace Quotinator.Data.Enums;

/// <summary>
/// What one layer of the database check found (#490, ADR 028).
/// </summary>
public enum DatabaseCheckLayerStatus
{
    /// <summary>The layer ran and found nothing wrong.</summary>
    Passed,

    /// <summary>The layer ran and found the database at fault.</summary>
    Failed,

    /// <summary>
    /// The layer could not be run soundly, for example because the copy it needed could not be made. It
    /// proves nothing either way, and is therefore never treated as a pass.
    /// </summary>
    Inconclusive,

    /// <summary>The layer was not run, because an earlier layer failed or was inconclusive.</summary>
    NotRun,

    /// <summary>The layer was not run, because the depth the check was asked for excludes it.</summary>
    Skipped,
}
