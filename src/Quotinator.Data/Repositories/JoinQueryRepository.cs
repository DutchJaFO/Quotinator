using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>Executes a join query defined by an <see cref="IJoinStrategy{TResult}"/> and returns the projected read models.</summary>
/// <typeparam name="TResult">The read model type returned by the query.</typeparam>
public class JoinQueryRepository<TResult>(
    IDbConnectionFactory   factory,
    IJoinStrategy<TResult> strategy)
{
    /// <summary>Returns all rows matching the strategy's SQL with optional Dapper parameters.</summary>
    public async Task<IReadOnlyList<TResult>> QueryAsync(object? parameters = null)
    {
        using var conn = factory.CreateConnection();
        conn.Open();
        return (await conn.QueryAsync<TResult>(strategy.BuildSql(), parameters)).ToList();
    }
}
