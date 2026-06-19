using Dapper;
using Quotinator.Data.Data;
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
    public async Task<IReadOnlyList<T>> GetDeletedAsync()
    {
        using var conn = Factory.CreateConnection();
        conn.Open();
        var results = await conn.QueryAsync<T>(
            $"SELECT * FROM {TableName} WHERE IsDeleted = 1");
        return results.ToList();
    }

    /// <inheritdoc/>
    public async Task RestoreAsync(Guid id)
    {
        var now = SafeDateValue.Now.Raw;
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.ExecuteAsync(
            $"UPDATE {TableName} SET IsDeleted = 0, DateDeleted = NULL, DateModified = @now WHERE Id = @id AND IsDeleted = 1",
            new { now, id = id.ToString("D").ToUpperInvariant() });
    }

    /// <inheritdoc/>
    public async Task HardDeleteAsync(Guid id)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.ExecuteAsync(
            $"DELETE FROM {TableName} WHERE Id = @id AND IsDeleted = 1",
            new { id = id.ToString("D").ToUpperInvariant() });
    }

    /// <inheritdoc/>
    public async Task<int> PurgeAsync()
    {
        using var conn = Factory.CreateConnection();
        conn.Open();
        return await conn.ExecuteAsync(
            $"DELETE FROM {TableName} WHERE IsDeleted = 1");
    }
}
