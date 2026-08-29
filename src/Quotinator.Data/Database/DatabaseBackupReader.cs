using Quotinator.Data.Enums;
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
    public BackupReadOutcome TryOpenRead(string name, out Stream? stream)
    {
        stream = null;

        if (!BackupFileNames.IsBackup(name) || !BackupFileNames.TryResolve(options.BackupsPath, name, out string fullPath))
            return BackupReadOutcome.InvalidName;

        if (!File.Exists(fullPath))
            return BackupReadOutcome.NotFound;

        try
        {
            // FileShare.ReadWrite, not Read: a reader that refuses writers cannot open a file some
            // other handle already holds writable, which is how a download failed against a backup
            // being written. Nothing here writes, so tolerating one costs nothing.
            stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return BackupReadOutcome.Opened;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BackupReadOutcome.NotReadable;
        }
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
