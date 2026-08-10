using Quotinator.Data.Helpers;
using Quotinator.Core.Queries;
using Quotinator.Data.Repositories;

namespace Quotinator.Core.Repositories;

/// <inheritdoc cref="ICharacterSourceLinkReader"/>
/// <summary>Initialises the reader with its join-query repositories — per ADR 017, SQL execution goes
/// through <see cref="JoinQueryRepository{TResult}"/>/<see cref="Quotinator.Data.Queries.IJoinStrategy{TResult}"/> rather than a raw connection.</summary>
/// <param name="referenceRepository">Executes the single-Character active linked-Sources join.</param>
/// <param name="batchRepository">Executes the batched active linked-Sources join.</param>
public sealed class CharacterSourceLinkReader(
    JoinQueryRepository<SourceRow> referenceRepository,
    JoinQueryRepository<LinkRow> batchRepository) : ICharacterSourceLinkReader
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<(Guid Id, string Name)>> GetSourceReferencesAsync(Guid characterId)
    {
        var rows = await referenceRepository.QueryAsync(new { characterId });
        return [.. rows.Select(r => (r.Id, r.Title))];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>> GetSourceReferencesForManyAsync(IReadOnlyList<Guid> characterIds)
    {
        if (characterIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>();

        // Dapper's list-parameter expansion does not reliably invoke a registered Guid ITypeHandler the
        // way a scalar parameter does — pre-canonicalize to strings before binding an IN-list (see
        // GuidExtensions.ToCanonicalId's remarks and ConversationLineCountReader's identical fix).
        var canonicalIds = characterIds.Select(id => id.ToCanonicalId());
        var rows = await batchRepository.QueryAsync(new { characterIds = canonicalIds });

        return rows.GroupBy(r => r.CharacterId)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<(Guid Id, string Name)>)[.. g.Select(r => (r.SourceId, r.SourceTitle))]);
    }
}
