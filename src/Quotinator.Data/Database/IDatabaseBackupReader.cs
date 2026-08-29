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
    /// Opens one backup file for reading, or returns <see langword="null"/> when no such file exists.
    /// The caller owns the returned stream.
    /// </summary>
    /// <param name="name">The file name, as <see cref="List"/> reports it.</param>
    /// <returns>A readable stream, or <see langword="null"/> when the name is unsafe or absent.</returns>
    Stream? OpenRead(string name);

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
