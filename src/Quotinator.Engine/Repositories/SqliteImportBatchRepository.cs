using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Queries;

namespace Quotinator.Engine.Repositories;

/// <summary>SQLite implementation of <see cref="IImportBatchRepository"/>.</summary>
public sealed class SqliteImportBatchRepository : SqliteRepository<ImportBatch>, IImportBatchRepository
{
    /// <inheritdoc/>
    public SqliteImportBatchRepository(IDbConnectionFactory factory, ISystemAuditWriter auditWriter, ICallerContext callerContext)
        : base(factory, auditWriter, callerContext) { }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ImportBatch>> GetAllAsync(IUnitOfWork? unitOfWork = null)
    {
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            var rows = await uow.Connection.QueryAsync<ImportBatch>(
                Sql.ImportBatches.SelectAll, transaction: uow.Transaction);
            return rows.ToList();
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        var results = await conn.QueryAsync<ImportBatch>(Sql.ImportBatches.SelectAll);
        return results.ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ImportBatch>> GetByTypeAsync(ImportBatchType type, IUnitOfWork? unitOfWork = null)
    {
        var param = new { type = type.ToString() };
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            var rows = await uow.Connection.QueryAsync<ImportBatch>(
                Sql.ImportBatches.SelectByType, param, uow.Transaction);
            return rows.ToList();
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        var results = await conn.QueryAsync<ImportBatch>(Sql.ImportBatches.SelectByType, param);
        return results.ToList();
    }

    /// <inheritdoc/>
    public async Task UpdateRecordCountAsync(Guid id, int count, IUnitOfWork? unitOfWork = null)
    {
        var param = new { count, now = SafeDateValue.Now.Raw, id = id.ToString("D").ToUpperInvariant() };
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            await uow.Connection.ExecuteAsync(Sql.ImportBatches.UpdateRecordCount, param, uow.Transaction);
            return;
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.ExecuteAsync(Sql.ImportBatches.UpdateRecordCount, param);
    }
}
