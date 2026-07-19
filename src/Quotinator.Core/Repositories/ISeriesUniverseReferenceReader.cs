namespace Quotinator.Core.Repositories;

/// <summary>Resolves a Series' UniverseId to its Universe's (Id, Name), filtered to an active
/// (non-deleted) Universe only — never writes.</summary>
public interface ISeriesUniverseReferenceReader
{
    /// <summary>The linked Universe's (Id, Name) for one Series, or <c>null</c> if the Series has no
    /// Universe or its Universe has been soft-deleted.</summary>
    Task<(Guid Id, string Name)?> GetUniverseReferenceAsync(Guid seriesId);

    /// <summary>The linked Universe's (Id, Name) for each of the given Series, in one round-trip. A
    /// Series with no active Universe link is absent from the result rather than mapped to a null entry —
    /// callers default missing keys to <c>null</c>.</summary>
    Task<IReadOnlyDictionary<Guid, (Guid Id, string Name)>> GetUniverseReferencesForManyAsync(IReadOnlyList<Guid> seriesIds);
}
