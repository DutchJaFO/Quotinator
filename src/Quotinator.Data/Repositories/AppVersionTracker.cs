using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <inheritdoc/>
/// <remarks>Initialises the tracker with the main database's connection factory.</remarks>
/// <param name="factory">Factory used to open SQLite connections.</param>
public sealed class AppVersionTracker(IDbConnectionFactory factory) : IAppVersionTracker
{
    /// <inheritdoc/>
    public async Task<string?> GetLastActiveVersionAsync()
    {
        try
        {
            using SqliteConnection connection = (SqliteConnection)factory.CreateConnection();
            await connection.OpenAsync();
            (Guid Id, string Version)? row = await connection.QuerySingleOrDefaultAsync<(Guid, string)?>(Sql.AppVersion.SelectCurrent);
            return row?.Version;
        }
        catch (SqliteException ex) when (IsMissingAppVersionTable(ex))
        {
            // Read before migrations run (see IAppVersionTracker's own remarks) — a missing table here
            // is the expected, normal state on the very first boot after this table was introduced, not
            // a genuine error. Matches #293's NotificationReader idiom.
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task RecordCurrentVersionAsync(string version)
    {
        using SqliteConnection connection = (SqliteConnection)factory.CreateConnection();
        await connection.OpenAsync();

        (Guid Id, string Version)? existing = await connection.QuerySingleOrDefaultAsync<(Guid, string)?>(Sql.AppVersion.SelectCurrent);
        if (existing is null)
        {
            await connection.InsertAsync(new AppVersionEntity { Version = version });
            return;
        }

        await connection.ExecuteAsync(
            Sql.AppVersion.UpdateVersionById,
            new { id = existing.Value.Id, version, dateModified = SafeDateValue.Now.Raw });
    }

    // Matches #293's NotificationReader.IsMissingNotificationTable idiom exactly: SqliteErrorCode 1
    // (SQLITE_ERROR) covers many message shapes, so the message text is also checked to stay narrowly
    // scoped to this one table.
    private static bool IsMissingAppVersionTable(SqliteException ex) =>
        ex.SqliteErrorCode == 1
        && ex.Message.Contains("no such table: System_AppVersion", StringComparison.Ordinal);
}
