namespace Quotinator.Core.Models;

/// <summary>
/// Whether a backup can be taken right now, and where storage stands against both limits that govern
/// it (#349).
/// </summary>
public sealed record BackupStatusResponse
{
    /// <summary>Whether a backup can be taken at this moment.</summary>
    public required bool CanBackUp { get; init; }

    /// <summary>
    /// Which obstacle is in the way, or <see langword="null"/> when a backup is possible. One of the
    /// backup outcome names, so it reads the same here as it does in a refused reset.
    /// </summary>
    public string? Obstacle { get; init; }

    /// <summary>What that obstacle means, in one sentence an operator can act on. Null when possible.</summary>
    public string? Cause { get; init; }

    /// <summary>What can be done about it, most actionable first. Empty when a backup is possible.</summary>
    public required IReadOnlyList<string> Remedies { get; init; }

    /// <summary>The quota picture and real free disk space.</summary>
    public required BackupStorageResponse Storage { get; init; }
}

/// <summary>
/// The storage half of <see cref="BackupStatusResponse"/> — the self-imposed quota and the physical
/// disk, reported side by side because they are independent and the backup path checks both.
/// </summary>
public sealed record BackupStorageResponse
{
    /// <summary>Total size of every backup file, in bytes.</summary>
    public required long UsedBytes { get; init; }

    /// <summary>How many backup files that total covers.</summary>
    public required int FileCount { get; init; }

    /// <summary>What routine operation may use, in bytes.</summary>
    public required long QuotaBytes { get; init; }

    /// <summary>The absolute ceiling, in bytes.</summary>
    public required long CeilingBytes { get; init; }

    /// <summary>The operating quota in force, as a percentage of the ceiling.</summary>
    public required int QuotaPercent { get; init; }

    /// <summary>Bytes left before the operating quota is reached.</summary>
    public required long RemainingAgainstQuotaBytes { get; init; }

    /// <summary>Bytes left before the absolute ceiling is reached.</summary>
    public required long RemainingAgainstCeilingBytes { get; init; }

    /// <summary>How much of the ceiling is used, as a percentage.</summary>
    public required double UsedPercentOfCeiling { get; init; }

    /// <summary>Whether usage has passed the quota and is relying on the reserve beneath the ceiling.</summary>
    public required bool ReserveInUse { get; init; }

    /// <summary>Real free space on the volume holding the backups folder, in bytes.</summary>
    public required long FreeDiskBytes { get; init; }
}
