using Quotinator.Core.Repositories;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="ISourceSeasonReferenceReader"/> double, backed by a constructor-supplied
/// Source id → Season reference dictionary. A Source id absent from the dictionary resolves to
/// <c>null</c>/no entry, matching the real reader's "absent, not null-valued" contract — this doubles for
/// both "no Season" and "Season soft-deleted", since the real reader's contract makes the two
/// indistinguishable to its caller by design.</summary>
internal sealed class FakeSourceSeasonReferenceReader : ISourceSeasonReferenceReader
{
    private readonly IReadOnlyDictionary<Guid, (Guid Id, int Number, string? Title, string? Subtitle)> _seasonBySourceId;

    internal FakeSourceSeasonReferenceReader(IReadOnlyDictionary<Guid, (Guid Id, int Number, string? Title, string? Subtitle)>? seed = null)
    {
        _seasonBySourceId = seed ?? new Dictionary<Guid, (Guid Id, int Number, string? Title, string? Subtitle)>();
    }

    public Task<(Guid Id, int Number, string? Title, string? Subtitle)?> GetSeasonReferenceAsync(Guid sourceId)
    {
        (Guid Id, int Number, string? Title, string? Subtitle)? result = _seasonBySourceId.TryGetValue(sourceId, out (Guid Id, int Number, string? Title, string? Subtitle) season) ? season : null;
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<Guid, (Guid Id, int Number, string? Title, string? Subtitle)>> GetSeasonReferencesForManyAsync(IReadOnlyList<Guid> sourceIds)
    {
        Dictionary<Guid, (Guid Id, int Number, string? Title, string? Subtitle)> result = sourceIds
            .Where(_seasonBySourceId.ContainsKey)
            .ToDictionary(id => id, id => _seasonBySourceId[id]);
        return Task.FromResult<IReadOnlyDictionary<Guid, (Guid Id, int Number, string? Title, string? Subtitle)>>(result);
    }
}
