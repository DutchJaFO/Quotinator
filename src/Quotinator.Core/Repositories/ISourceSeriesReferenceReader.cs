namespace Quotinator.Core.Repositories;

/// <summary>Resolves a Source's SeriesId to its Series' (Id, Name), filtered to an active (non-deleted)
/// Series only — never writes.</summary>
public interface ISourceSeriesReferenceReader
{
    /// <summary>The linked Series' (Id, Name) for one Source, or <c>null</c> if the Source has no Series
    /// or its Series has been soft-deleted.</summary>
    Task<(Guid Id, string Name)?> GetSeriesReferenceAsync(Guid sourceId);

    /// <summary>The linked Series' (Id, Name) for each of the given Sources, in one round-trip. A Source
    /// with no active Series link is absent from the result rather than mapped to a null entry — callers
    /// default missing keys to <c>null</c>.</summary>
    Task<IReadOnlyDictionary<Guid, (Guid Id, string Name)>> GetSeriesReferencesForManyAsync(IReadOnlyList<Guid> sourceIds);
}
