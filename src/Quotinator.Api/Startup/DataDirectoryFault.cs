using Microsoft.Data.Sqlite;

namespace Quotinator.Api.Startup;

/// <summary>
/// Recognises a data directory that cannot be written (#326), wherever initialisation first meets it: startup, or
/// the migration the operator resumes through <c>POST /api/v1/admin/database/migrate</c> (#490 step 17). Moved
/// out of <c>Program.cs</c> for that second caller, rather than copied.
/// </summary>
internal static class DataDirectoryFault
{
    /// <summary>
    /// Whether <paramref name="exception"/>, or any exception it wraps, says the data directory cannot be written.
    /// Walks the chain because the failure can arrive nested: DatabaseInitializer restores its backup and rethrows
    /// on any migration exception, so the SqliteException that actually describes the cause is not always the
    /// outermost one.
    /// </summary>
    /// <param name="exception">The exception to examine.</param>
    internal static bool IsNotWritable(Exception? exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            // 14 SQLITE_CANTOPEN: the directory cannot be written, so SQLite cannot create the file or
            // its -shm wal-index. 8 SQLITE_READONLY: the directory is writable but the file is not.
            if (current is SqliteException sqlite && sqlite.SqliteErrorCode is 14 or 8) return true;
            if (current is UnauthorizedAccessException or IOException) return true;
        }

        return false;
    }
}
