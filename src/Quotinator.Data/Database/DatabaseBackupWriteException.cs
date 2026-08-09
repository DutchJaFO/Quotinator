namespace Quotinator.Data.Database;

/// <summary>Thrown when writing a pre-migration/pre-seed backup file fails after the storage pre-flight check already passed — a real I/O failure, distinguishable from a failure in the migration/seed step itself.</summary>
public sealed class DatabaseBackupWriteException(string backupPath, Exception innerException)
    : Exception($"Failed to write backup file '{backupPath}'.", innerException)
{
    /// <summary>The backup file path that failed to write.</summary>
    public string BackupPath { get; } = backupPath;
}
