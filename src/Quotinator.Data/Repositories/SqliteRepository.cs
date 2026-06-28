using Dapper;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IRepository{T}"/> using Dapper and Dapper.Contrib.
/// Extends <see cref="SqliteRepositoryBase{T}"/> and writes an <see cref="AuditEntry"/> in the same
/// connection and transaction on every write operation.
/// Derive from <see cref="SqliteRepositoryBase{T}"/> directly — not from this class — when audit
/// recursion must be avoided (e.g. <see cref="AuditWriter"/>).
/// </summary>
/// <typeparam name="T">Entity type. Must carry a <c>[Table]</c> attribute from Dapper.Contrib.Extensions.</typeparam>
public class SqliteRepository<T> : SqliteRepositoryBase<T>, IRepository<T> where T : RecordBase
{
    private readonly IAuditWriter   _auditWriter;
    private readonly ICallerContext _callerContext;

    /// <summary>Initialises the repository with the factory, audit writer, and caller context.</summary>
    public SqliteRepository(IDbConnectionFactory factory, IAuditWriter auditWriter, ICallerContext callerContext)
        : base(factory)
    {
        _auditWriter   = auditWriter;
        _callerContext = callerContext;
    }

    /// <inheritdoc/>
    public async Task<T?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        var param = new { id = id.ToString("D").ToUpperInvariant() };
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            var results = await uow.Connection.QueryAsync<T>(
                RepositorySql.SelectById(TableName), param, uow.Transaction);
            return results.FirstOrDefault();
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        var rows = await conn.QueryAsync<T>(RepositorySql.SelectById(TableName), param);
        return rows.FirstOrDefault();
    }

    /// <inheritdoc/>
    public virtual async Task InsertAsync(T entity, IUnitOfWork? unitOfWork = null)
    {
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            await uow.Connection.InsertAsync(entity, uow.Transaction);
            await _auditWriter.WriteAsync(BuildEntry(AuditOperation.Insert, entity.Id), uow.Connection, uow.Transaction);
            return;
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.InsertAsync(entity);
        await _auditWriter.WriteAsync(BuildEntry(AuditOperation.Insert, entity.Id), conn);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(T entity, IUnitOfWork? unitOfWork = null)
    {
        entity.DateModified = SafeDateValue.Now;
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            await uow.Connection.UpdateAsync(entity, uow.Transaction);
            await _auditWriter.WriteAsync(BuildEntry(AuditOperation.Update, entity.Id), uow.Connection, uow.Transaction);
            return;
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.UpdateAsync(entity);
        await _auditWriter.WriteAsync(BuildEntry(AuditOperation.Update, entity.Id), conn);
    }

    /// <inheritdoc/>
    public async Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        var param = new { now = SafeDateValue.Now.Raw, id = id.ToString("D").ToUpperInvariant() };
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            await uow.Connection.ExecuteAsync(
                RepositorySql.SoftDelete(TableName), param, uow.Transaction);
            await _auditWriter.WriteAsync(BuildEntry(AuditOperation.SoftDelete, id), uow.Connection, uow.Transaction);
            return;
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.ExecuteAsync(RepositorySql.SoftDelete(TableName), param);
        await _auditWriter.WriteAsync(BuildEntry(AuditOperation.SoftDelete, id), conn);
    }

    /// <inheritdoc/>
    public override async Task InsertManyAsync(
        IEnumerable<T> entities,
        IUnitOfWork? unitOfWork = null,
        InsertStrategy strategy = InsertStrategy.Bulk)
    {
        var list = entities.ToList();
        await TransactionScope.ExecuteAsync(Factory, async uow =>
        {
            var sqlite = (SqliteUnitOfWork)uow;
            if (strategy == InsertStrategy.Bulk)
            {
                await sqlite.Connection.InsertAsync(list, sqlite.Transaction);
                var auditEntries = list.Select(e => BuildEntry(AuditOperation.Insert, e.Id)).ToList();
                await _auditWriter.WriteAsync(auditEntries, sqlite.Connection, sqlite.Transaction);
            }
            else
            {
                foreach (var entity in list)
                    await InsertAsync(entity, uow);
            }
        }, unitOfWork);
    }

    /// <summary>
    /// Writes an audit entry for a write operation on this table using an existing connection.
    /// Accessible to derived classes that implement additional write operations.
    /// </summary>
    protected Task WriteAuditAsync(string operation, Guid? id,
        System.Data.IDbConnection connection, System.Data.IDbTransaction? transaction = null)
        => _auditWriter.WriteAsync(BuildEntry(operation, id), connection, transaction);

    private AuditEntry BuildEntry(string operation, Guid? id) => new()
    {
        TableName   = TableName,
        RecordId    = id.HasValue ? id.Value.ToString("D").ToUpperInvariant() : null,
        Operation   = operation,
        Agent       = _callerContext.Agent,
        PerformedAt = DateTime.UtcNow,
    };
}
