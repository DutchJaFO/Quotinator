using Quotinator.Core.Repositories;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="ISeriesUniverseReferenceReader"/> double, backed by a constructor-supplied
/// Series id → Universe reference dictionary. A Series id absent from the dictionary resolves to
/// <c>null</c>/no entry, matching the real reader's "absent, not null-valued" contract — this doubles for
/// both "no Universe" and "Universe soft-deleted", since the real reader's contract makes the two
/// indistinguishable to its caller by design.</summary>
internal sealed class FakeSeriesUniverseReferenceReader : ISeriesUniverseReferenceReader
{
    private readonly IReadOnlyDictionary<Guid, (Guid Id, string Name)> _universeBySeriesId;

    internal FakeSeriesUniverseReferenceReader(IReadOnlyDictionary<Guid, (Guid Id, string Name)>? seed = null)
    {
        _universeBySeriesId = seed ?? new Dictionary<Guid, (Guid Id, string Name)>();
    }

    public Task<(Guid Id, string Name)?> GetUniverseReferenceAsync(Guid seriesId)
    {
        (Guid Id, string Name)? result = _universeBySeriesId.TryGetValue(seriesId, out var universe) ? universe : null;
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<Guid, (Guid Id, string Name)>> GetUniverseReferencesForManyAsync(IReadOnlyList<Guid> seriesIds)
    {
        var result = seriesIds
            .Where(_universeBySeriesId.ContainsKey)
            .ToDictionary(id => id, id => _universeBySeriesId[id]);
        return Task.FromResult<IReadOnlyDictionary<Guid, (Guid Id, string Name)>>(result);
    }
}
