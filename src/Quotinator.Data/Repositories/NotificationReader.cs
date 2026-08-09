using Dapper;
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
        var rows = await conn.QueryAsync<NotificationEntity>(Sql.Notifications.SelectActive, new { now });
        return [.. rows];
    }

    /// <inheritdoc/>
    public async Task<PagedItems<NotificationEntity>> GetPagedAsync(int page, int pageSize)
    {
        var limit  = pageSize == 0 ? -1 : pageSize;
        var offset = pageSize == 0 ? 0  : (page - 1) * pageSize;

        using var conn = Factory.CreateConnection();
        conn.Open();

        var total = await conn.ExecuteScalarAsync<int>(Sql.Notifications.CountAll);
        var items = (await conn.QueryAsync<NotificationEntity>(
            Sql.Notifications.SelectPage, new { pageSize = limit, offset })).ToList();

        var effectivePageSize = pageSize == 0 ? items.Count : pageSize;
        return new PagedItems<NotificationEntity>(items, page, effectivePageSize, total);
    }
}
