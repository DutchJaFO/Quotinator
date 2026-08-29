namespace Quotinator.Data.Models;

/// <summary>
/// One backup file in the backups folder (#349) — the facts an operator needs to decide which to keep
/// and which to remove, and nothing else.
/// </summary>
public sealed record BackupFileInfo
{
    /// <summary>File name only, never a path. This is the identifier every other backup endpoint takes.</summary>
    public required string Name { get; init; }

    /// <summary>Size of the file on disk, in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// When the file was written, in UTC. Read from the filesystem rather than parsed out of the
    /// name: a file placed here by #353's upload endpoint carries an operator-chosen name that need
    /// not follow the <c>_v{N}_{timestamp}Z</c> convention the application's own backups use.
    /// </summary>
    public required DateTime TakenAtUtc { get; init; }
}
