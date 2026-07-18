using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Engine.Queries;

namespace Quotinator.Engine.Repositories;

/// <inheritdoc cref="IConversationLineCountReader"/>
public sealed class ConversationLineCountReader : IConversationLineCountReader
{
    private readonly IDbConnectionFactory _factory;

    /// <summary>Initialises the reader with the connection factory.</summary>
    public ConversationLineCountReader(IDbConnectionFactory factory) => _factory = factory;

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, int>> GetLineCountsForManyAsync(IReadOnlyList<Guid> conversationIds)
    {
        if (conversationIds.Count == 0)
            return new Dictionary<Guid, int>();

        using var conn = _factory.CreateConnection();
        conn.Open();
        var rows = await conn.QueryAsync<LineCountRow>(
            Sql.ConversationLines.SelectLineCountsForConversations, new { conversationIds });
        return rows.ToDictionary(r => r.ConversationId, r => r.LineCount);
    }

    private sealed record LineCountRow(Guid ConversationId, int LineCount);
}
