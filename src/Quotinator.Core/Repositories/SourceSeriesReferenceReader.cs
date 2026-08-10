using Quotinator.Data.Helpers;
using Quotinator.Core.Queries;
using Quotinator.Data.Repositories;

namespace Quotinator.Core.Repositories;

/// <summary>SQLite implementation of <see cref="ISourceSeriesReferenceReader"/>.</summary>
/// <remarks>Initialises the reader with its join-query repositories — per ADR 017, SQL execution goes
/// through <see cref="JoinQueryRepository{TResult}"/>/<see cref="Quotinator.Data.Queries.IJoinStrategy{TResult}"/> rather than a raw connection.</remarks>
/// <param name="referenceRepository">Executes the single-Source active Series reference join.</param>
/// <param name="batchRepository">Executes the batched active Series reference join.</param>
public sealed class SourceSeriesReferenceReader(
    JoinQueryRepository<SeriesReferenceRow> referenceRepository,
    JoinQueryRepository<SourceSeriesReferenceRow> batchRepository) : ISourceSeriesReferenceReader
{
    /// <inheritdoc/>
    public async Task<(Guid Id, string Name)?> GetSeriesReferenceAsync(Guid sourceId)
    {
        var rows = await referenceRepository.QueryAsync(new { sourceId = sourceId.ToCanonicalId() });
        var row = rows.Count > 0 ? rows[0] : null;

        return row is null ? null : (row.Id, row.Name);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, (Guid Id, string Name)>> GetSeriesReferencesForManyAsync(IReadOnlyList<Guid> sourceIds)
    {
        if (sourceIds.Count == 0)
            return new Dictionary<Guid, (Guid Id, string Name)>();

        var canonicalIds = sourceIds.Select(id => id.ToCanonicalId()).ToList();
        var rows = await batchRepository.QueryAsync(new { sourceIds = canonicalIds });

        return rows.ToDictionary(r => r.SourceId, r => (r.SeriesId, r.SeriesName));
    }
}
