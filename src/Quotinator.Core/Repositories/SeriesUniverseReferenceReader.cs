using Quotinator.Data.Helpers;
using Quotinator.Core.Queries;
using Quotinator.Data.Repositories;

namespace Quotinator.Core.Repositories;

/// <summary>SQLite implementation of <see cref="ISeriesUniverseReferenceReader"/>.</summary>
/// <remarks>Initialises the reader with its join-query repositories — per ADR 017, SQL execution goes
/// through <see cref="JoinQueryRepository{TResult}"/>/<see cref="Quotinator.Data.Queries.IJoinStrategy{TResult}"/> rather than a raw connection.</remarks>
/// <param name="referenceRepository">Executes the single-Series active Universe reference join.</param>
/// <param name="batchRepository">Executes the batched active Universe reference join.</param>
public sealed class SeriesUniverseReferenceReader(
    JoinQueryRepository<UniverseReferenceRow> referenceRepository,
    JoinQueryRepository<SeriesUniverseReferenceRow> batchRepository) : ISeriesUniverseReferenceReader
{
    /// <inheritdoc/>
    public async Task<(Guid Id, string Name)?> GetUniverseReferenceAsync(Guid seriesId)
    {
        var rows = await referenceRepository.QueryAsync(new { seriesId = seriesId.ToCanonicalId() });
        var row = rows.Count > 0 ? rows[0] : null;

        return row is null ? null : (row.Id, row.Name);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, (Guid Id, string Name)>> GetUniverseReferencesForManyAsync(IReadOnlyList<Guid> seriesIds)
    {
        if (seriesIds.Count == 0)
            return new Dictionary<Guid, (Guid Id, string Name)>();

        var canonicalIds = seriesIds.Select(id => id.ToCanonicalId()).ToList();
        var rows = await batchRepository.QueryAsync(new { seriesIds = canonicalIds });

        return rows.ToDictionary(r => r.SeriesId, r => (r.UniverseId, r.UniverseName));
    }
}
