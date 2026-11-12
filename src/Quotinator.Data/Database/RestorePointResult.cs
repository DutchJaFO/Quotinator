using Quotinator.Data.Enums;

namespace Quotinator.Data.Database;

/// <summary>
/// The restore point an operation that replaces data left behind (#348): the backup it took, the file it
/// set aside, or that it took none and why.
/// <para>
/// Returned to the caller rather than only logged and audited. A log line rotates and an audit row is
/// indirect evidence at best; the consumer that asked for the operation is the one that has to know there
/// is no restore point, or it cannot act on that.
/// </para>
/// </summary>
public sealed class RestorePointResult
{
    /// <summary>What was left behind to restore from.</summary>
    public required RestorePointOutcome Outcome { get; init; }

    /// <summary>
    /// The file name of the backup taken, or of the database file set aside; <see langword="null"/> when
    /// the backup was skipped.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>Why no backup could be taken, when it was skipped; otherwise <see langword="null"/>.</summary>
    public BackupOutcome? Obstacle { get; init; }

    /// <summary>A backup was taken.</summary>
    /// <param name="fileName">The backup file's name.</param>
    public static RestorePointResult BackedUp(string fileName) =>
        new() { Outcome = RestorePointOutcome.Succeeded, FileName = fileName };

    /// <summary>No backup was taken, by the caller's explicit permission.</summary>
    /// <param name="obstacle">Why none could be taken.</param>
    public static RestorePointResult Skipped(BackupOutcome obstacle) =>
        new() { Outcome = RestorePointOutcome.Skipped, Obstacle = obstacle };

    /// <summary>The unreadable database file was moved aside unchanged (#482).</summary>
    /// <param name="fileName">The set-aside file's name.</param>
    public static RestorePointResult SetAside(string fileName) =>
        new() { Outcome = RestorePointOutcome.SetAside, FileName = fileName };
}
