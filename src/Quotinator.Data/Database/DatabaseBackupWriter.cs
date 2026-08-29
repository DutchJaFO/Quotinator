namespace Quotinator.Data.Database;

/// <summary>Filesystem-backed <see cref="IDatabaseBackupWriter"/> (#349).</summary>
/// <param name="options">Database options carrying the backups folder.</param>
public sealed class DatabaseBackupWriter(DatabaseOptions options) : IDatabaseBackupWriter
{
    /// <inheritdoc/>
    public bool Delete(string name)
    {
        // The guard runs here too, not only at the endpoint. A deletion is irreversible, and a second
        // caller reaching this writer without the endpoint's validation must not be the moment that is
        // discovered.
        if (!BackupFileNames.IsBackup(name)
            || !BackupFileNames.TryResolve(options.BackupsPath, name, out string fullPath)
            || !File.Exists(fullPath))
            return false;

        File.Delete(fullPath);
        return true;
    }
}
