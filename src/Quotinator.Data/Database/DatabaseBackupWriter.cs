using Quotinator.Data.Enums;

namespace Quotinator.Data.Database;

/// <summary>Filesystem-backed <see cref="IDatabaseBackupWriter"/> (#349).</summary>
/// <param name="options">Database options carrying the backups folder.</param>
public sealed class DatabaseBackupWriter(DatabaseOptions options) : IDatabaseBackupWriter
{
    /// <inheritdoc/>
    public BackupDeleteOutcome Delete(string name)
    {
        // The guard runs here too, not only at the endpoint. A deletion is irreversible, and a second
        // caller reaching this writer without the endpoint's validation must not be the moment that is
        // discovered.
        if (!BackupFileNames.IsBackup(name)
            || !BackupFileNames.TryResolve(options.BackupsPath, name, out string fullPath))
            return BackupDeleteOutcome.InvalidName;

        if (!File.Exists(fullPath))
            return BackupDeleteOutcome.NotFound;

        try
        {
            File.Delete(fullPath);
        }
        // Narrow rather than a bare catch: these are the two the filesystem raises when it will not
        // permit the removal, and they have a remedy an operator can act on. Anything else is genuinely
        // unforeseen and is left to propagate rather than being reported as a permission problem it is
        // not.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BackupDeleteOutcome.NotRemovable;
        }

        return BackupDeleteOutcome.Deleted;
    }
}
