namespace Quotinator.Data.Models;

/// <summary>
/// Where backup storage currently stands against both of the limits that govern it (#349) — the
/// self-imposed quota and the physical disk.
/// <para>
/// Both are reported because they are independent, and the backup path checks both: a folder well
/// inside its quota on a volume with no free space cannot take a backup, and neither can a folder at
/// its quota on an empty disk. Showing one would answer half the question, and would mislead exactly
/// when the other is the binding constraint.
/// </para>
/// </summary>
public sealed record BackupStorageUsage
{
    /// <summary>Total size of every file in the backups folder, in bytes.</summary>
    public required long UsedBytes { get; init; }

    /// <summary>How many backup files that total covers.</summary>
    public required int FileCount { get; init; }

    /// <summary>
    /// What normal operation may use, in bytes — <see cref="QuotaPercent"/> of
    /// <see cref="CeilingBytes"/>. A routine backup stops here rather than at the ceiling.
    /// </summary>
    public required long QuotaBytes { get; init; }

    /// <summary>The absolute ceiling, in bytes, never exceeded by any caller.</summary>
    public required long CeilingBytes { get; init; }

    /// <summary>
    /// The operating quota actually in force, as a percentage of the ceiling. This is the effective
    /// value: a configured percentage outside 1–100 is reported and the default used instead, so what
    /// this reports can differ from what is configured.
    /// </summary>
    public required int QuotaPercent { get; init; }

    /// <summary>Bytes remaining before the operating quota is reached. Never negative.</summary>
    public required long RemainingAgainstQuotaBytes { get; init; }

    /// <summary>Bytes remaining before the absolute ceiling is reached. Never negative.</summary>
    public required long RemainingAgainstCeilingBytes { get; init; }

    /// <summary>How much of the ceiling is used, as a percentage.</summary>
    public required double UsedPercentOfCeiling { get; init; }

    /// <summary>
    /// Whether usage has passed the operating quota and is now inside the reserve between quota and
    /// ceiling. True means routine backups are already being refused, and only a caller who explicitly
    /// reaches into the reserve can still take one.
    /// </summary>
    public required bool ReserveInUse { get; init; }

    /// <summary>
    /// Real free space on the volume holding the backups folder, in bytes — a physical constraint,
    /// independent of every quota figure above it.
    /// </summary>
    public required long FreeDiskBytes { get; init; }
}
