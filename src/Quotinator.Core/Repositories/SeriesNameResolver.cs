using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Core.Queries;

namespace Quotinator.Core.Repositories;

/// <summary>SQLite implementation of <see cref="ISeriesNameResolver"/>.</summary>
/// <remarks>Initialises the resolver with the connection factory.</remarks>
/// <param name="factory">Factory used to open SQLite connections.</param>
public sealed class SeriesNameResolver(IDbConnectionFactory factory) : ISeriesNameResolver
{
    private readonly IDbConnectionFactory _factory = factory;

    /// <inheritdoc/>
    public async Task<Guid?> ResolveIdByNameAsync(string name)
    {
        using var conn = _factory.CreateConnection();
        conn.Open();
        return await conn.ExecuteScalarAsync<Guid?>(Sql.Series.SelectIdByName, new { name });
    }
}
