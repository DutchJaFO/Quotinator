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
    /// Whether the operation ran without a backup because the caller explicitly accepted that. Recorded
    /// so a later "where is the backup" question has an answer that is not guesswork.
    /// </summary>
    public bool BackupSkippedByOverride { get; init; }

    /// <summary>
    /// Whether the operation stopped because the database's schema does not match the one its migration
    /// path defines (#305). The differences are in <see cref="Schema.IIntegrityCheckedDatabase.LastIntegrityResult"/>;
    /// a caller maps them to what an operator reads.
    /// </summary>
    public bool SchemaNotIntact { get; init; }

    /// <summary>
    /// An operation that stopped because the schema does not match its migration path (#305): nothing was
    /// loaded into it and nothing was repaired.
    /// </summary>
    public static DatabaseOperationResult NotIntact() =>
        new()
        { Succeeded = false, SchemaNotIntact = true };

    /// <summary>A successful operation.</summary>
    /// <param name="backupSkippedByOverride">Whether it proceeded without a backup by explicit override.</param>
    public static DatabaseOperationResult Success(bool backupSkippedByOverride = false) =>
        new()
        { Succeeded = true, BackupSkippedByOverride = backupSkippedByOverride };

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
