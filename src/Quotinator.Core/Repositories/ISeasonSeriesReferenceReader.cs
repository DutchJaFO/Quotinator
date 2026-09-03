namespace Quotinator.Core.Repositories;

/// <summary>Resolves a Season's SeriesId to its Series' (Id, Name), filtered to an active
/// (non-deleted) Series only — never writes. Mirrors <see cref="ISeriesUniverseReferenceReader"/>.</summary>
public interface ISeasonSeriesReferenceReader
{
    /// <summary>The linked Series' (Id, Name) for one Season, or <c>null</c> if the Season has no
    /// Series or its Series has been soft-deleted.</summary>
    Task<(Guid Id, string Name)?> GetSeriesReferenceAsync(Guid seasonId);

    /// <summary>The linked Series' (Id, Name) for each of the given Seasons, in one round-trip. A
    /// Season with no active Series link is absent from the result rather than mapped to a null entry —
    /// callers default missing keys to <c>null</c>.</summary>
    Task<IReadOnlyDictionary<Guid, (Guid Id, string Name)>> GetSeriesReferencesForManyAsync(IReadOnlyList<Guid> seasonIds);
}
