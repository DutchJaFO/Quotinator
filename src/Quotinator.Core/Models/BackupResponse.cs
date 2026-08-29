namespace Quotinator.Core.Models;

/// <summary>One backup file, as the backup endpoints report it (#349).</summary>
public sealed record BackupResponse
{
    /// <summary>File name. This is the identifier the delete and download endpoints take.</summary>
    public required string Name { get; init; }

    /// <summary>Size of the file on disk, in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>When the backup was written, in UTC.</summary>
    public required DateTime TakenAtUtc { get; init; }
}
