using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="INotificationReader"/>. Extends
/// <see cref="SqliteRepositoryBase{T}"/> directly — no audit writes are triggered by reads.
/// </summary>
/// <remarks>Initialises the reader with the connection factory.</remarks>
/// <param name="factory">Factory used to open SQLite connections.</param>
public sealed class NotificationReader(IDbConnectionFactory factory) : SqliteRepositoryBase<NotificationEntity>(factory), INotificationReader
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<NotificationEntity>> GetActiveNotificationsAsync()
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        var now = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
        try
        {
            var rows = await conn.QueryAsync<NotificationEntity>(Sql.Notifications.SelectActive, new { now });
            return [.. rows];
        }
        catch (SqliteException ex) when (IsMissingNotificationTable(ex))
        {
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<PagedItems<NotificationEntity>> GetPagedAsync(int page, int pageSize)
    {
        var limit  = pageSize == 0 ? -1 : pageSize;
        var offset = pageSize == 0 ? 0  : (page - 1) * pageSize;

        using var conn = Factory.CreateConnection();
        conn.Open();

        try
        {
            var total = await conn.ExecuteScalarAsync<int>(Sql.Notifications.CountAll);
            var items = (await conn.QueryAsync<NotificationEntity>(
                Sql.Notifications.SelectPage, new { pageSize = limit, offset })).ToList();

            var effectivePageSize = pageSize == 0 ? items.Count : pageSize;
            return new PagedItems<NotificationEntity>(items, page, effectivePageSize, total);
        }
        catch (SqliteException ex) when (IsMissingNotificationTable(ex))
        {
            return new PagedItems<NotificationEntity>([], page, pageSize, 0);
        }
    }

    // Not exception-based migration recovery (CLAUDE.md's "No exception-based migration recovery"
    // policy governs InitialiseAsync's own version-vs-schema mismatch handling, which stays a hard
    // failure) — this is a read-only, display-only query reached from Home's degraded-state modal and
    // the /notifications page, both of which stay reachable while the database is genuinely mid- or
    // pre-migration (#263/#280's whole point). System_Notification not existing yet in that state is
    // an expected, already-logged condition, not a structural mismatch to interpret; "no active
    // notifications" is the only correct response, not a 500 that defeats the degraded-state UI's
    // purpose. SqliteErrorCode 1 (SQLITE_ERROR) covers many message shapes, so the message text is
    // also checked to stay narrowly scoped to this one table.
    private static bool IsMissingNotificationTable(SqliteException ex)
        => ex.SqliteErrorCode == 1
           && ex.Message.Contains("no such table: System_Notification", StringComparison.Ordinal);
}
