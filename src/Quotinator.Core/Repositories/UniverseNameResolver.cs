using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Core.Queries;

namespace Quotinator.Core.Repositories;

/// <summary>SQLite implementation of <see cref="IUniverseNameResolver"/>.</summary>
public sealed class UniverseNameResolver : IUniverseNameResolver
{
    private readonly IDbConnectionFactory _factory;

    /// <summary>Initialises the resolver with the connection factory.</summary>
    public UniverseNameResolver(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <inheritdoc/>
    public async Task<Guid?> ResolveIdByNameAsync(string name)
    {
        using var conn = _factory.CreateConnection();
        conn.Open();
        return await conn.ExecuteScalarAsync<Guid?>(Sql.Universe.SelectIdByName, new { name });
    }
}
