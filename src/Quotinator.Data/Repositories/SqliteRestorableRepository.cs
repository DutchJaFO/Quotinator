using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Extends <see cref="SqliteRepository{T}"/> with soft-delete management:
/// retrieving deleted records, undoing a soft-delete, hard-deleting a single record,
/// and purging all soft-deleted records from the table.
/// </summary>
/// <typeparam name="T">Entity type. Must carry a <c>[Table]</c> attribute from Dapper.Contrib.Extensions.</typeparam>
public class SqliteRestorableRepository<T> : SqliteRepository<T>, IRestorableRepository<T>
    where T : RecordBase
{
    /// <summary>Initialises the repository with the factory used to open SQLite connections.</summary>
    /// <param name="factory">Opens connections to the SQLite database.</param>
    public SqliteRestorableRepository(IDbConnectionFactory factory) : base(factory) { }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<T>> GetDeletedAsync(IUnitOfWork? unitOfWork = null)
    {
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            var results = await uow.Connection.QueryAsync<T>(
                RepositorySql.SelectDeleted(TableName), transaction: uow.Transaction);
            return results.ToList();
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        var rows = await conn.QueryAsync<T>(RepositorySql.SelectDeleted(TableName));
        return rows.ToList();
    }

    /// <inheritdoc/>
    public async Task RestoreAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        var param = new { now = SafeDateValue.Now.Raw, id = id.ToString("D").ToUpperInvariant() };
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            await uow.Connection.ExecuteAsync(
                RepositorySql.Restore(TableName), param, uow.Transaction);
            return;
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.ExecuteAsync(RepositorySql.Restore(TableName), param);
    }

    /// <inheritdoc/>
    public async Task HardDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        var param = new { id = id.ToString("D").ToUpperInvariant() };
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            await uow.Connection.ExecuteAsync(
                RepositorySql.HardDelete(TableName), param, uow.Transaction);
            return;
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.ExecuteAsync(RepositorySql.HardDelete(TableName), param);
    }

    /// <inheritdoc/>
    public async Task<int> PurgeAsync(IUnitOfWork? unitOfWork = null)
    {
        if (unitOfWork is SqliteUnitOfWork uow)
            return await uow.Connection.ExecuteAsync(
                RepositorySql.Purge(TableName), transaction: uow.Transaction);
        using var conn = Factory.CreateConnection();
        conn.Open();
        return await conn.ExecuteAsync(RepositorySql.Purge(TableName));
    }
}
