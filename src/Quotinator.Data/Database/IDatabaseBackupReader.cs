using Quotinator.Data.Enums;
using Quotinator.Data.Models;

namespace Quotinator.Data.Database;

/// <summary>
/// Reads the backups folder (#349) — what is in it, how much room is left, and the bytes of one file.
/// <para>
/// Exists so no endpoint handler reaches into the filesystem itself. Every method here is safe to call
/// while the database is degraded: none of them opens the database, which is the whole point — the
/// operator who most needs to see their backups is the one whose database will not start.
/// </para>
/// </summary>
public interface IDatabaseBackupReader
{
    /// <summary>
    /// Every backup file that exists, newest first. An absent or empty folder is an empty list, not an
    /// error — nothing has been backed up yet.
    /// </summary>
    IReadOnlyList<BackupFileInfo> List();

    /// <summary>Where storage stands against the quota, the ceiling and real free disk space.</summary>
    BackupStorageUsage GetUsage();

    /// <summary>
    /// Opens one backup file for reading, reporting what happened rather than throwing.
    /// <para>
    /// A file that cannot be opened is an ordinary condition with a remedy, not an unforeseen fault.
    /// Letting it escape as an exception is what produced an unhandled <c>500</c> when a download met
    /// a locked file.
    /// </para>
    /// </summary>
    /// <param name="name">The file name, as <see cref="List"/> reports it.</param>
    /// <param name="stream">The readable stream when the outcome is <see cref="BackupReadOutcome.Opened"/>; otherwise <see langword="null"/>. The caller owns it.</param>
    /// <returns>Which of the four outcomes occurred.</returns>
    BackupReadOutcome TryOpenRead(string name, out Stream? stream);

    /// <summary>Whether a backup with this name currently exists and is safe to act on.</summary>
    /// <param name="name">The file name, as <see cref="List"/> reports it.</param>
    bool Exists(string name);

    /// <summary>
    /// Whether this is a name this application will act on at all — a file name resolving inside the
    /// backups folder, rather than a path, a traversal, or anything carrying a separator.
    /// <para>
    /// Exposed so a caller can tell "you may not ask for that" from "that does not exist" and answer
    /// each differently, without taking its own dependency on where the backups folder is. The guard
    /// itself still runs inside every method here regardless of whether a caller checked first.
    /// </para>
    /// </summary>
    /// <param name="name">The caller-supplied identifier.</param>
    bool IsValidName(string name);
}
