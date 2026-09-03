namespace Quotinator.Core.Repositories;

/// <summary>Resolves a Source's SeasonId to its Season's (Id, Number, Title, Subtitle), filtered to an
/// active (non-deleted) Season only — never writes.</summary>
public interface ISourceSeasonReferenceReader
{
    /// <summary>The linked Season's (Id, Number, Title, Subtitle) for one Source, or <c>null</c> if the
    /// Source has no Season or its Season has been soft-deleted.</summary>
    Task<(Guid Id, int Number, string? Title, string? Subtitle)?> GetSeasonReferenceAsync(Guid sourceId);

    /// <summary>The linked Season's (Id, Number, Title, Subtitle) for each of the given Sources, in one
    /// round-trip. A Source with no active Season link is absent from the result rather than mapped to a
    /// null entry — callers default missing keys to <c>null</c>.</summary>
    Task<IReadOnlyDictionary<Guid, (Guid Id, int Number, string? Title, string? Subtitle)>> GetSeasonReferencesForManyAsync(IReadOnlyList<Guid> sourceIds);
}
