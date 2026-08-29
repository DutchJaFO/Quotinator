using Quotinator.Data.Enums;

namespace Quotinator.Data.Database;

/// <summary>
/// Removes a backup file (#349).
/// <para>
/// Separate from <see cref="IDatabaseBackupReader"/> so the destructive capability is injected only
/// where it is actually used, matching the reader/writer split every other pair in this project
/// follows. Deletion is the only operation here: taking a backup belongs to
/// <see cref="IDatabaseInitializer"/>, which already owns the connection and the version it is taken
/// at, and restoring one belongs to #352.
/// </para>
/// </summary>
public interface IDatabaseBackupWriter
{
    /// <summary>
    /// Removes one backup file, reporting what happened rather than throwing.
    /// <para>
    /// A filesystem that refuses the removal is an ordinary operating condition with a remedy — the
    /// same category as a full backup folder — not an unforeseen fault. Letting it escape as an
    /// exception is what produced an unhandled <c>500</c> on a read-only data directory, which is
    /// precisely the state an operator is in when they are told to remove old backups.
    /// </para>
    /// </summary>
    /// <param name="name">The file name, as <see cref="IDatabaseBackupReader.List"/> reports it.</param>
    /// <returns>Which of the four outcomes occurred.</returns>
    BackupDeleteOutcome Delete(string name);
}
