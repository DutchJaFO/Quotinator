using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="ISystemChangeLogReader"/>.
/// Extends <see cref="SqliteRepositoryBase{T}"/> directly — no audit writes are triggered by reads.
/// </summary>
public sealed class SystemChangeLogReader : SqliteRepositoryBase<SystemChangeLog>, ISystemChangeLogReader
{
    /// <summary>Initialises the reader with the connection factory.</summary>
    public SystemChangeLogReader(IDbConnectionFactory factory) : base(factory) { }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SystemChangeLog>> GetHistoryAsync(string entityType, string entityId)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        var rows = await conn.QueryAsync<SystemChangeLog>(
            Sql.SystemChangeLog.SelectByEntity, new { entityType, entityId });

        return rows.ToList();
    }
}
