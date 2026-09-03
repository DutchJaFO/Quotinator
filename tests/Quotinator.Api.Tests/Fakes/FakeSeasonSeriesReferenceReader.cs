using Quotinator.Core.Repositories;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="ISeasonSeriesReferenceReader"/> double, backed by a constructor-supplied
/// Season id → Series reference dictionary. A Season id absent from the dictionary resolves to
/// <c>null</c>/no entry, matching the real reader's "absent, not null-valued" contract — this doubles for
/// both "no Series" and "Series soft-deleted", since the real reader's contract makes the two
/// indistinguishable to its caller by design.</summary>
internal sealed class FakeSeasonSeriesReferenceReader : ISeasonSeriesReferenceReader
{
    private readonly IReadOnlyDictionary<Guid, (Guid Id, string Name)> _seriesBySeasonId;

    internal FakeSeasonSeriesReferenceReader(IReadOnlyDictionary<Guid, (Guid Id, string Name)>? seed = null)
    {
        _seriesBySeasonId = seed ?? new Dictionary<Guid, (Guid Id, string Name)>();
    }

    public Task<(Guid Id, string Name)?> GetSeriesReferenceAsync(Guid seasonId)
    {
        (Guid Id, string Name)? result = _seriesBySeasonId.TryGetValue(seasonId, out (Guid Id, string Name) series) ? series : null;
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<Guid, (Guid Id, string Name)>> GetSeriesReferencesForManyAsync(IReadOnlyList<Guid> seasonIds)
    {
        Dictionary<Guid, (Guid Id, string Name)> result = seasonIds
            .Where(_seriesBySeasonId.ContainsKey)
            .ToDictionary(id => id, id => _seriesBySeasonId[id]);
        return Task.FromResult<IReadOnlyDictionary<Guid, (Guid Id, string Name)>>(result);
    }
}
