using Quotinator.Data.Helpers;
using Quotinator.Core.Queries;
using Quotinator.Data.Repositories;

namespace Quotinator.Core.Repositories;

/// <summary>SQLite implementation of <see cref="ISourceSeasonReferenceReader"/>.</summary>
/// <remarks>Initialises the reader with its join-query repositories — per ADR 017, SQL execution goes
/// through <see cref="JoinQueryRepository{TResult}"/>/<see cref="Quotinator.Data.Queries.IJoinStrategy{TResult}"/> rather than a raw connection.</remarks>
/// <param name="referenceRepository">Executes the single-Source active Season reference join.</param>
/// <param name="batchRepository">Executes the batched active Season reference join.</param>
public sealed class SourceSeasonReferenceReader(
    JoinQueryRepository<SourceSeasonReferenceRow> referenceRepository,
    JoinQueryRepository<SourceSeasonReferencesBatchRow> batchRepository) : ISourceSeasonReferenceReader
{
    /// <inheritdoc/>
    public async Task<(Guid Id, int Number, string? Title, string? Subtitle)?> GetSeasonReferenceAsync(Guid sourceId)
    {
        IReadOnlyList<SourceSeasonReferenceRow> rows = await referenceRepository.QueryAsync(new { sourceId = sourceId.ToCanonicalId() });
        SourceSeasonReferenceRow? row = rows.Count > 0 ? rows[0] : null;

        return row is null ? null : (row.Id, (int)row.Number, row.Title, row.Subtitle);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, (Guid Id, int Number, string? Title, string? Subtitle)>> GetSeasonReferencesForManyAsync(IReadOnlyList<Guid> sourceIds)
    {
        if (sourceIds.Count == 0)
            return new Dictionary<Guid, (Guid Id, int Number, string? Title, string? Subtitle)>();

        List<string> canonicalIds = [.. sourceIds.Select(id => id.ToCanonicalId())];
        IReadOnlyList<SourceSeasonReferencesBatchRow> rows = await batchRepository.QueryAsync(new { sourceIds = canonicalIds });

        return rows.ToDictionary(r => r.SourceId, r => (r.SeasonId, (int)r.Number, r.Title, r.Subtitle));
    }
}
