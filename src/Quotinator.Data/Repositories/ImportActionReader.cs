using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IImportActionReader"/>.
/// Extends <see cref="SqliteRepositoryBase{T}"/> directly — no audit writes are triggered by reads.
/// </summary>
public sealed class ImportActionReader : SqliteRepositoryBase<ImportActionEntity>, IImportActionReader
{
    /// <summary>Initialises the reader with the connection factory.</summary>
    public ImportActionReader(IDbConnectionFactory factory) : base(factory) { }

    /// <inheritdoc/>
    public async Task<PagedItems<ImportActionEntity>> GetPagedAsync(string? batchId, string? status, string? entityType, int page, int pageSize)
    {
        var filterBatchId    = batchId    is not null;
        var filterStatus     = status     is not null;
        var filterEntityType = entityType is not null;
        var limit             = pageSize == 0 ? -1 : pageSize;
        var offset            = pageSize == 0 ? 0  : (page - 1) * pageSize;

        using var conn = Factory.CreateConnection();
        conn.Open();

        var total = await conn.ExecuteScalarAsync<int>(
            Sql.SystemImportActions.CountPaged(filterBatchId, filterStatus, filterEntityType),
            new { batchId, status, entityType });

        var items = (await conn.QueryAsync<ImportActionEntity>(
            Sql.SystemImportActions.SelectPaged(filterBatchId, filterStatus, filterEntityType),
            new { batchId, status, entityType, pageSize = limit, offset })).ToList();

        var effectivePageSize = pageSize == 0 ? items.Count : pageSize;
        return new PagedItems<ImportActionEntity>(items, page, effectivePageSize, total);
    }

    /// <inheritdoc/>
    public async Task<ImportActionEntity?> GetByIdAsync(Guid id)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        return await conn.QueryFirstOrDefaultAsync<ImportActionEntity>(Sql.SystemImportActions.SelectById, new { id });
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ImportActionEntity>> GetAllForBatchAsync(string batchId)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        var items = await conn.QueryAsync<ImportActionEntity>(Sql.SystemImportActions.SelectAllForBatch, new { batchId });
        return [.. items];
    }
}
