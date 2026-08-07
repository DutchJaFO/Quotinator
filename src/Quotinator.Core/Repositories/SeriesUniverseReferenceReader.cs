using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Helpers;
using Quotinator.Core.Queries;

namespace Quotinator.Core.Repositories;

/// <summary>SQLite implementation of <see cref="ISeriesUniverseReferenceReader"/>.</summary>
/// <remarks>Initialises the reader with the connection factory.</remarks>
/// <param name="factory">Factory used to open SQLite connections.</param>
public sealed class SeriesUniverseReferenceReader(IDbConnectionFactory factory) : ISeriesUniverseReferenceReader
{
    private readonly IDbConnectionFactory _factory = factory;

    /// <inheritdoc/>
    public async Task<(Guid Id, string Name)?> GetUniverseReferenceAsync(Guid seriesId)
    {
        var param = new { seriesId = seriesId.ToCanonicalId() };

        using var conn = _factory.CreateConnection();
        conn.Open();
        var row = await conn.QueryFirstOrDefaultAsync<UniverseReferenceRow>(
            Sql.Series.SelectUniverseReferenceForSeries, param);

        return row is null ? null : (row.Id, row.Name);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, (Guid Id, string Name)>> GetUniverseReferencesForManyAsync(IReadOnlyList<Guid> seriesIds)
    {
        if (seriesIds.Count == 0)
            return new Dictionary<Guid, (Guid Id, string Name)>();

        var param = new { seriesIds = seriesIds.Select(id => id.ToCanonicalId()).ToList() };

        using var conn = _factory.CreateConnection();
        conn.Open();
        var rows = await conn.QueryAsync<SeriesUniverseReferenceRow>(
            Sql.Series.SelectUniverseReferencesForSeries, param);

        return rows.ToDictionary(r => r.SeriesId, r => (r.UniverseId, r.UniverseName));
    }

    private sealed record UniverseReferenceRow(Guid Id, string Name);

    private sealed record SeriesUniverseReferenceRow(Guid SeriesId, Guid UniverseId, string UniverseName);
}
