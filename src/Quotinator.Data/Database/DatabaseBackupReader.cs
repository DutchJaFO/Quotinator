using Quotinator.Data.Models;

namespace Quotinator.Data.Database;

/// <summary>Filesystem-backed <see cref="IDatabaseBackupReader"/> (#349).</summary>
/// <param name="options">Database options carrying the backups folder and the storage budget.</param>
/// <param name="diskSpaceProvider">Reports real free space on the volume holding the backups folder.</param>
public sealed class DatabaseBackupReader(DatabaseOptions options, IDiskSpaceProvider diskSpaceProvider)
    : IDatabaseBackupReader
{
    /// <inheritdoc/>
    public IReadOnlyList<BackupFileInfo> List()
    {
        if (!Directory.Exists(options.BackupsPath))
            return [];

        return
        [
            .. new DirectoryInfo(options.BackupsPath)
                 .EnumerateFiles()
                 .Where(f => BackupFileNames.IsBackup(f.Name))
                 .Select(f => new BackupFileInfo
                 {
                     Name       = f.Name,
                     SizeBytes  = f.Length,
                     TakenAtUtc = f.LastWriteTimeUtc,
                 })
                 // Newest first, because the question an operator opens this list with is "which of
                 // these is old enough to remove". The name is the tie-break rather than an arbitrary
                 // order: the application's own backup names end in a sortable timestamp, so two files
                 // sharing a filesystem timestamp still come back in a stable, meaningful sequence.
                 .OrderByDescending(b => b.TakenAtUtc)
                 .ThenByDescending(b => b.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <inheritdoc/>
    public BackupStorageUsage GetUsage()
    {
        long used     = BackupStorageBudget.UsedBytes(options.BackupsPath);
        long ceiling  = BackupStorageBudget.CeilingBytes(options);
        long quota    = BackupStorageBudget.QuotaBytes(options);
        int  percent  = BackupStorageBudget.EffectiveQuotaPercent(options, out _);
        int  count    = Directory.Exists(options.BackupsPath)
            ? Directory.EnumerateFiles(options.BackupsPath).Count()
            : 0;

        return new BackupStorageUsage
        {
            UsedBytes                    = used,
            FileCount                    = count,
            QuotaBytes                   = quota,
            CeilingBytes                 = ceiling,
            QuotaPercent                 = percent,
            RemainingAgainstQuotaBytes   = Math.Max(0L, quota - used),
            RemainingAgainstCeilingBytes = Math.Max(0L, ceiling - used),
            UsedPercentOfCeiling         = ceiling == 0L ? 0d : used * 100d / ceiling,
            ReserveInUse                 = used >= quota,
            FreeDiskBytes                = diskSpaceProvider.GetAvailableFreeSpaceBytes(options.BackupsPath),
        };
    }

    /// <inheritdoc/>
    public Stream? OpenRead(string name)
    {
        if (!Exists(name))
            return null;

        BackupFileNames.TryResolve(options.BackupsPath, name, out string fullPath);

        // FileShare.Read rather than None: a download must not block a concurrent backup from being
        // taken, and nothing here writes.
        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <inheritdoc/>
    public bool Exists(string name) =>
        BackupFileNames.IsBackup(name)
        && BackupFileNames.TryResolve(options.BackupsPath, name, out string fullPath)
        && File.Exists(fullPath);

    /// <inheritdoc/>
    public bool IsValidName(string name) =>
        BackupFileNames.TryResolve(options.BackupsPath, name, out _);
}
