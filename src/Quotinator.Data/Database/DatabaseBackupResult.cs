using Quotinator.Data.Enums;

namespace Quotinator.Data.Database;

/// <summary>
/// The result of one database backup attempt (#327) — which obstacle it hit, if any, and the file it
/// produced when it succeeded.
/// <para>
/// Replaces the <see langword="string"/>? this used to be. A null path meant "budget exceeded",
/// "insufficient disk space" and "no backup was attempted" all at once, which is precisely what stopped
/// a caller diagnosing anything: three different faults with three different remedies arrived
/// indistinguishable.
/// </para>
/// </summary>
public sealed class DatabaseBackupResult
{
    /// <summary>Which obstacle the attempt hit, or <see cref="BackupOutcome.Succeeded"/>.</summary>
    public required BackupOutcome Outcome { get; init; }

    /// <summary>
    /// The backup file that was written. Non-<see langword="null"/> exactly when
    /// <see cref="Outcome"/> is <see cref="BackupOutcome.Succeeded"/>.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// The underlying failure, where one was thrown. Carried rather than swallowed so an
    /// <see cref="BackupOutcome.Unclassified"/> outcome can still be reported precisely.
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>Whether a backup file now exists.</summary>
    public bool Succeeded => Outcome == BackupOutcome.Succeeded;

    /// <summary>A successful attempt, carrying the file it wrote.</summary>
    /// <param name="path">The backup file that was written.</param>
    public static DatabaseBackupResult Success(string path) =>
        new DatabaseBackupResult { Outcome = BackupOutcome.Succeeded, Path = path };

    /// <summary>A failed attempt, carrying why and the underlying error where there was one.</summary>
    /// <param name="outcome">Which obstacle the attempt hit.</param>
    /// <param name="error">The underlying failure, when one was thrown.</param>
    public static DatabaseBackupResult Failed(BackupOutcome outcome, Exception? error = null) =>
        new DatabaseBackupResult { Outcome = outcome, Error = error };
}
