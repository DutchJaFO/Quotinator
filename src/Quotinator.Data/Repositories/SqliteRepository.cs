using System.Reflection;
using Dapper;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Data;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IRepository{T}"/> using Dapper and Dapper.Contrib.
/// Each method opens its own connection when no <see cref="IUnitOfWork"/> is supplied.
/// When a <see cref="IUnitOfWork"/> is supplied, the operation runs on its connection and transaction.
/// All SQL is delegated to <see cref="RepositorySql"/> and fully parameterised.
/// </summary>
/// <typeparam name="T">Entity type. Must carry a <c>[Table]</c> attribute from Dapper.Contrib.Extensions.</typeparam>
public class SqliteRepository<T> : IRepository<T> where T : RecordBase
{
    /// <summary>Factory used to open connections. Accessible to derived repository classes.</summary>
    protected readonly IDbConnectionFactory Factory;

    // Resolved once per T. The table name comes from the [Table] attribute — developer-controlled
    // metadata, not user input. See RepositorySql for why interpolating it into SQL is safe.
    /// <summary>SQLite table name resolved from the <c>[Table]</c> attribute on <typeparamref name="T"/>. Accessible to derived repository classes.</summary>
    protected static readonly string TableName =
        typeof(T).GetCustomAttribute<TableAttribute>()?.Name
        ?? throw new InvalidOperationException(
            $"{typeof(T).Name} must carry a [Table(\"..\")] attribute from Dapper.Contrib.Extensions.");

    /// <summary>Initialises the repository with the factory used to open SQLite connections.</summary>
    /// <param name="factory">Opens connections to the SQLite database.</param>
    public SqliteRepository(IDbConnectionFactory factory)
    {
        Factory = factory;
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
    public async Task InsertAsync(T entity, IUnitOfWork? unitOfWork = null)
    {
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            await uow.Connection.InsertAsync(entity, uow.Transaction);
            return;
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.InsertAsync(entity);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(T entity, IUnitOfWork? unitOfWork = null)
    {
        entity.DateModified = SafeDateValue.Now;
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            await uow.Connection.UpdateAsync(entity, uow.Transaction);
            return;
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.UpdateAsync(entity);
    }

    /// <inheritdoc/>
    public async Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        var param = new { now = SafeDateValue.Now.Raw, id = id.ToString("D").ToUpperInvariant() };
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            await uow.Connection.ExecuteAsync(
                RepositorySql.SoftDelete(TableName), param, uow.Transaction);
            return;
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.ExecuteAsync(RepositorySql.SoftDelete(TableName), param);
    }
}
