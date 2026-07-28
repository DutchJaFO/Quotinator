using Quotinator.Core.Repositories;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="IConversationLineCountReader"/> double, backed by a constructor-supplied
/// Conversation id → line count dictionary. A Conversation id absent from the dictionary resolves to 0,
/// matching the real reader's "absent means zero" contract.</summary>
internal sealed class FakeConversationLineCountReader : IConversationLineCountReader
{
    private readonly IReadOnlyDictionary<Guid, int> _lineCountsByConversationId;

    internal FakeConversationLineCountReader(IReadOnlyDictionary<Guid, int>? seed = null)
    {
        _lineCountsByConversationId = seed ?? new Dictionary<Guid, int>();
    }

    public Task<IReadOnlyDictionary<Guid, int>> GetLineCountsForManyAsync(IReadOnlyList<Guid> conversationIds)
    {
        var result = conversationIds
            .Where(_lineCountsByConversationId.ContainsKey)
            .ToDictionary(id => id, id => _lineCountsByConversationId[id]);
        return Task.FromResult<IReadOnlyDictionary<Guid, int>>(result);
    }
}
