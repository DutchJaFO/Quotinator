using Quotinator.Data.Helpers;
using Quotinator.Core.Queries;
using Quotinator.Data.Repositories;

namespace Quotinator.Core.Repositories;

/// <summary>SQLite implementation of <see cref="ISeasonSeriesReferenceReader"/>.</summary>
/// <remarks>Initialises the reader with its join-query repositories — per ADR 017, SQL execution goes
/// through <see cref="JoinQueryRepository{TResult}"/>/<see cref="Quotinator.Data.Queries.IJoinStrategy{TResult}"/> rather than a raw connection.</remarks>
/// <param name="referenceRepository">Executes the single-Season active Series reference join.</param>
/// <param name="batchRepository">Executes the batched active Series reference join.</param>
public sealed class SeasonSeriesReferenceReader(
    JoinQueryRepository<SeasonSeriesReferenceRow> referenceRepository,
    JoinQueryRepository<SeasonSeriesReferencesBatchRow> batchRepository) : ISeasonSeriesReferenceReader
{
    /// <inheritdoc/>
    public async Task<(Guid Id, string Name)?> GetSeriesReferenceAsync(Guid seasonId)
    {
        IReadOnlyList<SeasonSeriesReferenceRow> rows = await referenceRepository.QueryAsync(new { seasonId = seasonId.ToCanonicalId() });
        SeasonSeriesReferenceRow? row = rows.Count > 0 ? rows[0] : null;

        return row is null ? null : (row.Id, row.Name);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, (Guid Id, string Name)>> GetSeriesReferencesForManyAsync(IReadOnlyList<Guid> seasonIds)
    {
        if (seasonIds.Count == 0)
            return new Dictionary<Guid, (Guid Id, string Name)>();

        List<string> canonicalIds = [.. seasonIds.Select(id => id.ToCanonicalId())];
        IReadOnlyList<SeasonSeriesReferencesBatchRow> rows = await batchRepository.QueryAsync(new { seasonIds = canonicalIds });

        return rows.ToDictionary(r => r.SeasonId, r => (r.SeriesId, r.SeriesName));
    }
}
