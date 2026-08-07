using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>SQLite implementation of <see cref="IImportBatchRepository"/>.</summary>
/// <remarks>Initialises the repository with the factory, audit writer, and caller context.</remarks>
/// <param name="factory">Factory used to open SQLite connections.</param>
/// <param name="auditWriter">Writer used to record an <see cref="AuditEntryEntity"/> for every write operation.</param>
/// <param name="callerContext">Identifies the caller attributed to each audit entry.</param>
public sealed class SqliteImportBatchRepository(IDbConnectionFactory factory, IAuditEntryWriter auditWriter, ICallerContext callerContext) : SqliteRepository<ImportBatchEntity>(factory, auditWriter, callerContext), IImportBatchRepository
{

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ImportBatchEntity>> GetAllAsync(IUnitOfWork? unitOfWork = null)
    {
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            var rows = await uow.Connection.QueryAsync<ImportBatchEntity>(
                Sql.ImportBatches.SelectAll, transaction: uow.Transaction);
            return [.. rows];
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        var results = await conn.QueryAsync<ImportBatchEntity>(Sql.ImportBatches.SelectAll);
        return [.. results];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ImportBatchEntity>> GetByTypeAsync(ImportBatchType type, IUnitOfWork? unitOfWork = null)
    {
        var param = new { type = type.ToString() };
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            var rows = await uow.Connection.QueryAsync<ImportBatchEntity>(
                Sql.ImportBatches.SelectByType, param, uow.Transaction);
            return [.. rows];
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        var results = await conn.QueryAsync<ImportBatchEntity>(Sql.ImportBatches.SelectByType, param);
        return [.. results];
    }

    /// <inheritdoc/>
    public async Task<PagedItems<ImportBatchEntity>> GetPagedAsync(ImportBatchType? type, ImportBatchStatus? status, int page, int pageSize)
    {
        var filterType   = type   is not null;
        var filterStatus = status is not null;
        var limit        = pageSize == 0 ? -1 : pageSize;
        var offset       = pageSize == 0 ? 0  : (page - 1) * pageSize;
        var param        = new { type = type?.ToString(), status = status?.ToString(), pageSize = limit, offset };

        using var conn = Factory.CreateConnection();
        conn.Open();

        var total = await conn.ExecuteScalarAsync<int>(Sql.ImportBatches.CountPaged(filterType, filterStatus), param);
        var items = (await conn.QueryAsync<ImportBatchEntity>(Sql.ImportBatches.SelectPaged(filterType, filterStatus), param)).ToList();

        var effectivePageSize = pageSize == 0 ? items.Count : pageSize;
        return new PagedItems<ImportBatchEntity>(items, page, effectivePageSize, total);
    }

    /// <inheritdoc/>
    public async Task UpdateRecordCountAsync(Guid id, int count, IUnitOfWork? unitOfWork = null)
    {
        var param = new { count, now = SafeDateValue.Now.Raw, id = id.ToCanonicalId() };
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
