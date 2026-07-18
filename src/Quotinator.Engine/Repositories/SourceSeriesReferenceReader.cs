using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Engine.Queries;

namespace Quotinator.Engine.Repositories;

/// <summary>SQLite implementation of <see cref="ISourceSeriesReferenceReader"/>.</summary>
public sealed class SourceSeriesReferenceReader : ISourceSeriesReferenceReader
{
    private readonly IDbConnectionFactory _factory;

    /// <summary>Initialises the reader with the connection factory.</summary>
    public SourceSeriesReferenceReader(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <inheritdoc/>
    public async Task<(Guid Id, string Name)?> GetSeriesReferenceAsync(Guid sourceId)
    {
        var param = new { sourceId = sourceId.ToString("D").ToUpperInvariant() };

        using var conn = _factory.CreateConnection();
        conn.Open();
        var row = await conn.QueryFirstOrDefaultAsync<SeriesReferenceRow>(
            Sql.Sources.SelectSeriesReferenceForSource, param);

        return row is null ? null : (row.Id, row.Name);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, (Guid Id, string Name)>> GetSeriesReferencesForManyAsync(IReadOnlyList<Guid> sourceIds)
    {
        if (sourceIds.Count == 0)
            return new Dictionary<Guid, (Guid Id, string Name)>();

        var param = new { sourceIds = sourceIds.Select(id => id.ToString("D").ToUpperInvariant()).ToList() };

        using var conn = _factory.CreateConnection();
        conn.Open();
        var rows = await conn.QueryAsync<SourceSeriesReferenceRow>(
            Sql.Sources.SelectSeriesReferencesForSources, param);

        return rows.ToDictionary(r => r.SourceId, r => (r.SeriesId, r.SeriesName));
    }

    private sealed record SeriesReferenceRow(Guid Id, string Name);

    private sealed record SourceSeriesReferenceRow(Guid SourceId, Guid SeriesId, string SeriesName);
}
