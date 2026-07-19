namespace Quotinator.Core.Repositories;

/// <summary>Reads the CharacterSources join for masterdata read endpoints (#185) — never writes. Returns
/// plain (Id, Name) tuples rather than <see cref="Models.MasterDataReference"/> directly — this reader
/// stays a data-shape concern, independent of which response DTO an individual endpoint chooses to build
/// from the tuple.</summary>
public interface ICharacterSourceLinkReader
{
    /// <summary>Active (Id, Title) references for every Source linked to one Character.</summary>
    Task<IReadOnlyList<(Guid Id, string Name)>> GetSourceReferencesAsync(Guid characterId);

    /// <summary>
    /// Active (Id, Title) Source references for each of the given Characters, in one round-trip. A
    /// Character with no links is absent from the result rather than mapped to an empty list — callers
    /// default missing keys to an empty array.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>> GetSourceReferencesForManyAsync(IReadOnlyList<Guid> characterIds);
}
