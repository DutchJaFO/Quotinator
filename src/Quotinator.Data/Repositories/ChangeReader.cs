using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IChangeReader"/>.
/// Extends <see cref="SqliteRepositoryBase{T}"/> directly — no audit writes are triggered by reads.
/// </summary>
public sealed class ChangeReader : SqliteRepositoryBase<ChangeEntity>, IChangeReader
{
    /// <summary>Initialises the reader with the connection factory.</summary>
    public ChangeReader(IDbConnectionFactory factory) : base(factory) { }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChangeEntity>> GetHistoryAsync(string entityType, string entityId)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        var rows = await conn.QueryAsync<ChangeEntity>(
            Sql.SystemChangeLog.SelectByEntity, new { entityType, entityId });

        return rows.ToList();
    }
}
