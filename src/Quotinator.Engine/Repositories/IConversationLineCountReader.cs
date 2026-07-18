namespace Quotinator.Engine.Repositories;

/// <summary>Resolves each Conversation's active line count for masterdata-style summary read endpoints
/// (#189) — never writes.</summary>
public interface IConversationLineCountReader
{
    /// <summary>Active line counts for each of the given Conversations, in one round-trip. A Conversation
    /// with zero active lines is absent from the result rather than mapped to a zero entry — callers
    /// default missing keys to 0.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetLineCountsForManyAsync(IReadOnlyList<Guid> conversationIds);
}
