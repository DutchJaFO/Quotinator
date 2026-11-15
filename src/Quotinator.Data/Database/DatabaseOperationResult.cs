using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Database;

/// <summary>
/// The outcome of an initialisation or reset (#348): whether it did what was asked, and if not, which
/// obstacle stopped it.
/// <para>
/// These operations used to return a bare <see cref="Task"/> and communicate failure by throwing.
/// Per developer direction an exception is for a condition there is no other way to detect; a backup
/// that cannot be taken is detected deliberately, before anything is attempted, so it is reported as a
/// result. Exceptions are still caught around these paths, as the backstop for what the check could not
/// foresee, not as the mechanism for what it did.
/// </para>
/// <para>
/// Deliberately carries a typed <see cref="BackupOutcome"/> rather than a message.
/// <c>Quotinator.Data</c> is domain-agnostic (ADR 004) and has no business deciding what an operator is
/// told; the consuming layer maps the outcome to a reason and its remedies, which is also where that
/// text can be localised.
/// </para>
/// </summary>
public sealed class DatabaseOperationResult
{
    /// <summary>Whether the operation did what was asked of it.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>
    /// Which backup obstacle stopped the operation, when one did. <see langword="null"/> when the
    /// operation succeeded, or when it failed for a reason unrelated to backups.
    /// </summary>
    public BackupOutcome? BackupObstacle { get; init; }

    /// <summary>
    /// What stopped a Reset of a database file that could not be read (#482), when that is what stopped
    /// it. Such a Reset takes no backup, so <see cref="BackupObstacle"/> is <see langword="null"/>
    /// alongside it: the two never describe the same refusal.
    /// </summary>
    public ResetObstacle? ResetObstacle { get; init; }

    /// <summary>
    /// Which guarded step refused, when one did: a caller reports a refused migration differently from a
    /// refused content load. <see langword="null"/> when nothing was refused.
    /// </summary>
    public BackupGuardedStep? RefusedStep { get; init; }

    /// <summary>
    /// Whether the operation ran without a backup because the caller explicitly accepted that: read from
    /// <see cref="RestorePoint"/>, so the two cannot disagree.
    /// </summary>
    public bool BackupSkippedByOverride => RestorePoint?.Outcome == RestorePointOutcome.Skipped;

    /// <summary>
    /// What a successful Reset left behind to restore from (#348): the backup it took, the file it set
    /// aside, or that it took none and why. <see langword="null"/> for an operation that replaces nothing.
    /// </summary>
    public RestorePointResult? RestorePoint { get; init; }

    /// <summary>
    /// Whether the operation stopped because the database's schema does not match the one its migration
    /// path defines (#305). The differences are in <see cref="Schema.IIntegrityCheckedDatabase.LastIntegrityResult"/>;
    /// a caller maps them to what an operator reads.
    /// </summary>
    public bool SchemaNotIntact { get; init; }

    /// <summary>
    /// The database check that ran before the operation opened the database for writing (#490, ADR 028),
    /// whatever it found, or <see langword="null"/> when none ran. When it stopped the operation,
    /// <see cref="Succeeded"/> is <see langword="false"/>. A finding that does not stop it, a References
    /// failure under decision E, is carried on a successful result, so a caller can report it without
    /// reading a log. Its <see cref="DatabaseCheckResult.FirstFailure"/> names the layer; a caller maps that
    /// to what an operator reads.
    /// </summary>
    public DatabaseCheckResult? Check { get; init; }

    /// <summary>
    /// An operation stopped by the database check (#490): the check failed or was inconclusive, so nothing
    /// was opened for writing, loaded or repaired.
    /// </summary>
    /// <param name="check">The check that stopped it.</param>
    public static DatabaseOperationResult StoppedByCheck(DatabaseCheckResult check) =>
        new()
        { Succeeded = false, Check = check };

    /// <summary>
    /// An operation that stopped because the schema does not match its migration path (#305): nothing was
    /// loaded into it and nothing was repaired.
    /// </summary>
    public static DatabaseOperationResult NotIntact() =>
        new()
        { Succeeded = false, SchemaNotIntact = true };

    /// <summary>A successful operation.</summary>
    /// <param name="restorePoint">What it left behind to restore from, when it replaced the database.</param>
    /// <param name="check">The database check that ran first, when one did (#490).</param>
    public static DatabaseOperationResult Success(RestorePointResult? restorePoint = null, DatabaseCheckResult? check = null) =>
        new()
        { Succeeded = true, RestorePoint = restorePoint, Check = check };

    /// <summary>An operation refused because no backup could be taken.</summary>
    /// <param name="obstacle">Which obstacle stopped it.</param>
    /// <param name="step">Which guarded step refused.</param>
    public static DatabaseOperationResult RefusedForBackup(BackupOutcome obstacle, BackupGuardedStep step) =>
        new()
        { Succeeded = false, BackupObstacle = obstacle, RefusedStep = step };

    /// <summary>
    /// A Reset refused because the database could not be replaced (#482): an unreadable file could not be
    /// set aside, or the data directory cannot be written. Nothing was moved or written.
    /// </summary>
    /// <param name="obstacle">What stopped it.</param>
    public static DatabaseOperationResult RefusedForReset(ResetObstacle obstacle) =>
        new()
        { Succeeded = false, ResetObstacle = obstacle, RefusedStep = BackupGuardedStep.Reset };
}
