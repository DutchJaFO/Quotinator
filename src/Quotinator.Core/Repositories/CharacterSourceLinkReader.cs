using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Helpers;
using Quotinator.Core.Queries;

namespace Quotinator.Core.Repositories;

/// <inheritdoc cref="ICharacterSourceLinkReader"/>
/// <summary>Initialises the reader with the connection factory.</summary>
/// <param name="factory">Factory used to open SQLite connections.</param>
public sealed class CharacterSourceLinkReader(IDbConnectionFactory factory) : ICharacterSourceLinkReader
{
    private readonly IDbConnectionFactory _factory = factory;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(Guid Id, string Name)>> GetSourceReferencesAsync(Guid characterId)
    {
        using var conn = _factory.CreateConnection();
        conn.Open();
        var rows = await conn.QueryAsync<SourceRow>(Sql.CharacterSources.SelectSourceReferencesForCharacter, new { characterId });
        return rows.Select(r => (r.Id, r.Title)).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>> GetSourceReferencesForManyAsync(IReadOnlyList<Guid> characterIds)
    {
        if (characterIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>();

        using var conn = _factory.CreateConnection();
        conn.Open();
        // Dapper's list-parameter expansion does not reliably invoke a registered Guid ITypeHandler the
        // way a scalar parameter does — pre-canonicalize to strings before binding an IN-list (see
        // GuidExtensions.ToCanonicalId's remarks and ConversationLineCountReader's identical fix).
        var canonicalIds = characterIds.Select(id => id.ToCanonicalId());
        var rows = await conn.QueryAsync<LinkRow>(Sql.CharacterSources.SelectSourceReferencesForCharacters, new { characterIds = canonicalIds });
        return rows.GroupBy(r => r.CharacterId)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<(Guid Id, string Name)>)[.. g.Select(r => (r.SourceId, r.SourceTitle))]);
    }

    private sealed record SourceRow(Guid Id, string Title);

    private sealed record LinkRow(Guid CharacterId, Guid SourceId, string SourceTitle);
}
