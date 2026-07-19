using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Core.Queries;

namespace Quotinator.Core.Repositories;

/// <inheritdoc cref="ICharacterSourceLinkReader"/>
public sealed class CharacterSourceLinkReader : ICharacterSourceLinkReader
{
    private readonly IDbConnectionFactory _factory;

    /// <summary>Initialises the reader with the connection factory.</summary>
    public CharacterSourceLinkReader(IDbConnectionFactory factory) => _factory = factory;

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
        var rows = await conn.QueryAsync<LinkRow>(Sql.CharacterSources.SelectSourceReferencesForCharacters, new { characterIds });
        return rows.GroupBy(r => r.CharacterId)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<(Guid Id, string Name)>)g.Select(r => (r.SourceId, r.SourceTitle)).ToList());
    }

    private sealed record SourceRow(Guid Id, string Title);

    private sealed record LinkRow(Guid CharacterId, Guid SourceId, string SourceTitle);
}
