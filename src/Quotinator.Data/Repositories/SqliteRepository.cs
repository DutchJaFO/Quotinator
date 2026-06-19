using System.Reflection;
using Dapper;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Data;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IRepository{T}"/> using Dapper and Dapper.Contrib.
/// Each method opens and closes its own connection via <see cref="IDbConnectionFactory"/>.
/// Exposes no raw SQL surface — all SQL is internal to this class and fully parameterised.
/// </summary>
/// <typeparam name="T">Entity type. Must carry a <c>[Table]</c> attribute from Dapper.Contrib.Extensions.</typeparam>
public class SqliteRepository<T> : IRepository<T> where T : RecordBase
{
    private readonly IDbConnectionFactory _factory;

    // Resolved once per T. The table name comes from the [Table] attribute — developer-controlled
    // metadata, not user input. The interpolation in SoftDeleteAsync is therefore safe.
    private static readonly string TableName =
        typeof(T).GetCustomAttribute<TableAttribute>()?.Name
        ?? throw new InvalidOperationException(
            $"{typeof(T).Name} must carry a [Table(\"..\")] attribute from Dapper.Contrib.Extensions.");

    /// <summary>Initialises the repository with the factory used to open SQLite connections.</summary>
    /// <param name="factory">Opens connections to the SQLite database.</param>
    public SqliteRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <inheritdoc/>
    public async Task<T?> GetByIdAsync(Guid id)
    {
        using var conn = _factory.CreateConnection();
        conn.Open();
        var results = await conn.QueryAsync<T>(
            $"SELECT * FROM {TableName} WHERE Id = @id AND IsDeleted = 0",
            new { id = id.ToString("D").ToUpperInvariant() });
        return results.FirstOrDefault();
    }

    /// <inheritdoc/>
    public async Task InsertAsync(T entity)
    {
        using var conn = _factory.CreateConnection();
        conn.Open();
        await conn.InsertAsync(entity);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(T entity)
    {
        entity.DateModified = SafeDateValue.Now;
        using var conn = _factory.CreateConnection();
        conn.Open();
        await conn.UpdateAsync(entity);
    }

    /// <inheritdoc/>
    public async Task SoftDeleteAsync(Guid id)
    {
        var now = SafeDateValue.Now.Raw;
        using var conn = _factory.CreateConnection();
        conn.Open();
        await conn.ExecuteAsync(
            $"UPDATE {TableName} SET IsDeleted = 1, DateDeleted = @now, DateModified = @now WHERE Id = @id AND IsDeleted = 0;",
            new { now, id = id.ToString("D").ToUpperInvariant() });
    }
}
