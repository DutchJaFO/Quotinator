using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="ISystemImportActionReader"/>.
/// Extends <see cref="SqliteRepositoryBase{T}"/> directly — no audit writes are triggered by reads.
/// </summary>
public sealed class SystemImportActionReader : SqliteRepositoryBase<SystemImportAction>, ISystemImportActionReader
{
    /// <summary>Initialises the reader with the connection factory.</summary>
    public SystemImportActionReader(IDbConnectionFactory factory) : base(factory) { }

    /// <inheritdoc/>
    public async Task<SystemImportActionPageResult> GetPagedAsync(string? batchId, string? status, int page, int pageSize)
    {
        var filterBatchId = batchId is not null;
        var filterStatus  = status  is not null;
        var offset        = (page - 1) * pageSize;

        using var conn = Factory.CreateConnection();
        conn.Open();

        var total = await conn.ExecuteScalarAsync<int>(
            Sql.SystemImportActions.CountPaged(filterBatchId, filterStatus),
            new { batchId, status });

        var items = await conn.QueryAsync<SystemImportAction>(
            Sql.SystemImportActions.SelectPaged(filterBatchId, filterStatus),
            new { batchId, status, pageSize, offset });

        return new SystemImportActionPageResult(items.ToList(), page, pageSize, total);
    }

    /// <inheritdoc/>
    public async Task<SystemImportAction?> GetByIdAsync(Guid id)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        return await conn.QueryFirstOrDefaultAsync<SystemImportAction>(Sql.SystemImportActions.SelectById, new { id });
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SystemImportAction>> GetAllForBatchAsync(string batchId)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        var items = await conn.QueryAsync<SystemImportAction>(Sql.SystemImportActions.SelectAllForBatch, new { batchId });
        return items.ToList();
    }
}
