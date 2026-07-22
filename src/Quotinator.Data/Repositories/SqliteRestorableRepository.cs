using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Extends <see cref="SqliteRepository{T}"/> with soft-delete management:
/// retrieving deleted records, undoing a soft-delete, hard-deleting a single record,
/// and purging all soft-deleted records from the table.
/// All write methods write a <see cref="SystemAuditEntry"/> in the same connection and transaction.
/// </summary>
/// <typeparam name="T">Entity type. Must carry a <c>[Table]</c> attribute from Dapper.Contrib.Extensions.</typeparam>
public class SqliteRestorableRepository<T> : SqliteRepository<T>, IRestorableRepository<T>
    where T : RecordBase
{
    /// <summary>Initialises the repository with the factory, audit writer, and caller context.</summary>
    /// <param name="factory">Opens connections to the SQLite database.</param>
    /// <param name="auditWriter">Writes an audit entry alongside every write operation.</param>
    /// <param name="callerContext">Provides the current caller's identity for audit entries.</param>
    public SqliteRestorableRepository(IDbConnectionFactory factory, ISystemAuditWriter auditWriter, ICallerContext callerContext)
        : base(factory, auditWriter, callerContext) { }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<T>> GetDeletedAsync(IUnitOfWork? unitOfWork = null)
    {
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            var results = await uow.Connection.QueryAsync<T>(
                RepositorySql.SelectDeleted(TableName, Columns), transaction: uow.Transaction);
            return results.ToList();
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        var rows = await conn.QueryAsync<T>(RepositorySql.SelectDeleted(TableName, Columns));
        return rows.ToList();
    }

    /// <inheritdoc/>
    public async Task RestoreAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        var param = new { now = SafeDateValue.Now.Raw, id = id.ToCanonicalId() };
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            await uow.Connection.ExecuteAsync(
                RepositorySql.Restore(TableName), param, uow.Transaction);
            await WriteAuditAsync(AuditOperation.Restore, id, uow.Connection, uow.Transaction);
            return;
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.ExecuteAsync(RepositorySql.Restore(TableName), param);
        await WriteAuditAsync(AuditOperation.Restore, id, conn);
    }

    /// <inheritdoc/>
    public async Task HardDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        var param = new { id = id.ToCanonicalId() };
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            await uow.Connection.ExecuteAsync(
                RepositorySql.HardDelete(TableName), param, uow.Transaction);
            await WriteAuditAsync(AuditOperation.HardDelete, id, uow.Connection, uow.Transaction);
            return;
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.ExecuteAsync(RepositorySql.HardDelete(TableName), param);
        await WriteAuditAsync(AuditOperation.HardDelete, id, conn);
    }

    /// <inheritdoc/>
    public async Task<int> PurgeAsync(IUnitOfWork? unitOfWork = null)
    {
        int count;
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            count = await uow.Connection.ExecuteAsync(
                RepositorySql.Purge(TableName), transaction: uow.Transaction);
            await WriteAuditAsync(AuditOperation.Purge, null, uow.Connection, uow.Transaction);
            return count;
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        count = await conn.ExecuteAsync(RepositorySql.Purge(TableName));
        await WriteAuditAsync(AuditOperation.Purge, null, conn);
        return count;
    }
}
