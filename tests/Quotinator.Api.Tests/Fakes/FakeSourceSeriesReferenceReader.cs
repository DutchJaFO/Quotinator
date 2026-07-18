using Quotinator.Engine.Repositories;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="ISourceSeriesReferenceReader"/> double, backed by a constructor-supplied
/// Source id → Series reference dictionary. A Source id absent from the dictionary resolves to
/// <c>null</c>/no entry, matching the real reader's "absent, not null-valued" contract — this doubles for
/// both "no Series" and "Series soft-deleted", since the real reader's contract makes the two
/// indistinguishable to its caller by design.</summary>
internal sealed class FakeSourceSeriesReferenceReader : ISourceSeriesReferenceReader
{
    private readonly IReadOnlyDictionary<Guid, (Guid Id, string Name)> _seriesBySourceId;

    internal FakeSourceSeriesReferenceReader(IReadOnlyDictionary<Guid, (Guid Id, string Name)>? seed = null)
    {
        _seriesBySourceId = seed ?? new Dictionary<Guid, (Guid Id, string Name)>();
    }

    public Task<(Guid Id, string Name)?> GetSeriesReferenceAsync(Guid sourceId)
    {
        (Guid Id, string Name)? result = _seriesBySourceId.TryGetValue(sourceId, out var series) ? series : null;
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<Guid, (Guid Id, string Name)>> GetSeriesReferencesForManyAsync(IReadOnlyList<Guid> sourceIds)
    {
        var result = sourceIds
            .Where(_seriesBySourceId.ContainsKey)
            .ToDictionary(id => id, id => _seriesBySourceId[id]);
        return Task.FromResult<IReadOnlyDictionary<Guid, (Guid Id, string Name)>>(result);
    }
}
