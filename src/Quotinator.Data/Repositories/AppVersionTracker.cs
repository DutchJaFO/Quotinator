using System.Data.Common;
using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <inheritdoc/>
/// <remarks>Initialises the tracker with the main database's connection factory.</remarks>
/// <param name="factory">Factory used to open SQLite connections.</param>
public sealed class AppVersionTracker(IDbConnectionFactory factory) : IAppVersionTracker
{
    /// <inheritdoc/>
    public async Task<AppVersionRecord?> GetLastActiveAsync()
    {
        try
        {
            using SqliteConnection connection = (SqliteConnection)factory.CreateConnection();
            await connection.OpenAsync();
            return await connection.QuerySingleOrDefaultAsync<AppVersionRecord?>(Sql.AppVersion.SelectMostRecent);
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
    public async Task<AppVersionRecord> RecordCurrentAsync(string application, string version)
    {
        using SqliteConnection connection = (SqliteConnection)factory.CreateConnection();
        await connection.OpenAsync();

        // Append-if-new, not upsert (#312). #81's original version overwrote a single row in place,
        // which would make a notification's provenance reference silently re-point at whatever version
        // ran most recently — the exact thing provenance must not do.
        AppVersionRecord? existing = await connection.QuerySingleOrDefaultAsync<AppVersionRecord?>(
            Sql.AppVersion.SelectByApplicationAndVersion, new { application, version });

        if (existing is not null)
            return existing;

        // The next sequence number and the insert that claims it share one transaction: read separately,
        // two startups racing could compute the same MAX + 1. The uniqueness index would still catch
        // that, but as a thrown constraint violation rather than a correct write.
        using DbTransaction transaction = await connection.BeginTransactionAsync();
        long sequenceNumber = await connection.ExecuteScalarAsync<long>(
            Sql.AppVersion.SelectNextSequenceNumber, transaction: transaction);

        AppVersionEntity entity = new() { Application = application, Version = version, SequenceNumber = sequenceNumber };
        await connection.InsertAsync(entity, transaction);
        await transaction.CommitAsync();

        return new AppVersionRecord(entity.Id, application, version);
    }

    // Matches #293's NotificationReader.IsMissingNotificationTable idiom exactly: SqliteErrorCode 1
    // (SQLITE_ERROR) covers many message shapes, so the message text is also checked to stay narrowly
    // scoped to this one table.
    private static bool IsMissingAppVersionTable(SqliteException ex) =>
        ex.SqliteErrorCode == 1
        && ex.Message.Contains("no such table: System_AppVersion", StringComparison.Ordinal);
}
