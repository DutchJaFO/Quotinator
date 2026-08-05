using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChangeEntity>> GetAllInRangeAsync(DateTime? startDate, DateTime? endDate)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        var rows = await conn.QueryAsync<ChangeEntity>(
            Sql.SystemChangeLog.SelectInRange(startDate is not null, endDate is not null),
            new { startDate, endDate });

        return rows.ToList();
    }

    /// <inheritdoc/>
    public async Task<int> CountInRangeAsync(DateTime? startDate, DateTime? endDate)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        return await conn.ExecuteScalarAsync<int>(
            Sql.SystemChangeLog.CountInRange(startDate is not null, endDate is not null),
            new { startDate, endDate });
    }

    /// <inheritdoc/>
    public async Task<(DateTime? Earliest, DateTime? Latest)> GetDateRangeAsync()
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        var row = await conn.QueryFirstOrDefaultAsync<DateRangeRow>(Sql.SystemChangeLog.SelectDateRange);
        return (row?.Earliest, row?.Latest);
    }
}
