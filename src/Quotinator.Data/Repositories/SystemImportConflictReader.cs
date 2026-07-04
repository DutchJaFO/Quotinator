using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="ISystemImportConflictReader"/>.
/// Extends <see cref="SqliteRepositoryBase{T}"/> directly — no audit writes are triggered by reads.
/// </summary>
public sealed class SystemImportConflictReader : SqliteRepositoryBase<SystemImportConflict>, ISystemImportConflictReader
{
    /// <summary>Initialises the reader with the connection factory.</summary>
    public SystemImportConflictReader(IDbConnectionFactory factory) : base(factory) { }

    /// <inheritdoc/>
    public async Task<SystemImportConflictPageResult> GetPagedAsync(string? batchId, string? status, int page, int pageSize)
    {
        var filterBatchId = batchId is not null;
        var filterStatus  = status  is not null;
        var offset        = (page - 1) * pageSize;

        using var conn = Factory.CreateConnection();
        conn.Open();

        var total = await conn.ExecuteScalarAsync<int>(
            Sql.SystemImportConflicts.CountPaged(filterBatchId, filterStatus),
            new { batchId, status });

        var items = await conn.QueryAsync<SystemImportConflict>(
            Sql.SystemImportConflicts.SelectPaged(filterBatchId, filterStatus),
            new { batchId, status, pageSize, offset });

        return new SystemImportConflictPageResult(items.ToList(), page, pageSize, total);
    }
}
