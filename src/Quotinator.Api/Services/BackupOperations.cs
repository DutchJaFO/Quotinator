using Quotinator.Api.Logging;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Services;

/// <summary>
/// Takes and removes backups the way an operator's own request does (#349), for every caller that acts
/// on an operator's behalf: logged, and recorded in the audit trail where it outlives the log.
/// <para>
/// Moved out of the backup endpoints when the notification's reseed action came to need the same two
/// operations (#348). Two copies of "remove a backup and record it" would drift, and the one that forgot
/// the audit entry would leave a restore point gone with no record of who removed it.
/// </para>
/// </summary>
/// <param name="databaseInitializer">Takes the backup, which needs the database connection and version it owns.</param>
/// <param name="backupWriter">Removes a backup file.</param>
/// <param name="auditWriter">Records each backup taken or removed.</param>
/// <param name="callerContext">Names who asked, for the audit entry.</param>
/// <param name="logger">Logs each outcome under the caller's own tag.</param>
internal sealed class BackupOperations(
    IDatabaseInitializer databaseInitializer,
    IDatabaseBackupWriter backupWriter,
    IAuditEntryWriter auditWriter,
    ICallerContext callerContext,
    ILogger<BackupOperations> logger)
{
    /// <summary>Takes a backup now, logging the outcome and auditing a success.</summary>
    /// <param name="tag">The caller's <c>[Subsystem - Phase]</c> prefix, so the log names who asked.</param>
    internal async Task<DatabaseBackupResult> CreateAsync(string tag)
    {
        DatabaseBackupResult result = await databaseInitializer.CreateBackupAsync();

        if (!result.Succeeded)
        {
            logger.LogBackupRefused(tag, result.Outcome.ToString());
            return result;
        }

        string name = Path.GetFileName(result.Path!);
        logger.LogBackupAction(tag, "created", name);

        // RecordId stays null per docs/logging.md's audit schema: an admin action is a database-level
        // operation, and RecordId is documented as the affected row's UUID. The file name is therefore
        // carried by the log line above rather than the audit row.
        await auditWriter.WriteAsync(new AuditEntryEntity
        {
            TableName   = "Database",
            Operation   = AuditOperation.Backup,
            Agent       = callerContext.Agent,
            PerformedAt = DateTime.UtcNow,
        });

        return result;
    }

    /// <summary>Removes one backup, logging the outcome and auditing a removal.</summary>
    /// <param name="name">The file name, as <see cref="IDatabaseBackupReader.List"/> reports it.</param>
    /// <param name="tag">The caller's <c>[Subsystem - Phase]</c> prefix, so the log names who asked.</param>
    internal async Task<BackupDeleteOutcome> RemoveAsync(string name, string tag)
    {
        BackupDeleteOutcome outcome = backupWriter.Delete(name);

        if (outcome is not BackupDeleteOutcome.Deleted)
        {
            logger.LogBackupRefused(tag, outcome.ToString());
            return outcome;
        }

        logger.LogBackupAction(tag, "removed", name);

        // Removing a backup removes a restore point, so it is recorded where it will still be found long
        // after the log has rotated: the reason removal goes through the application rather than by hand.
        await auditWriter.WriteAsync(new AuditEntryEntity
        {
            TableName   = "Database",
            Operation   = AuditOperation.BackupDeleted,
            Agent       = callerContext.Agent,
            PerformedAt = DateTime.UtcNow,
        });

        return outcome;
    }
}
