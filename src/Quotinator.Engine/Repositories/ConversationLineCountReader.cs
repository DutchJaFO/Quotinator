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

        // QueryAsync<dynamic>, not a positional record, is deliberate. Two independently-confirmed causes
        // (see 189-conversations-list-plan.md's Notes for sources):
        // 1. Dapper's registered SqlMapper.TypeHandler<Guid> (GuidHandler) is only invoked when the target
        //    type has a parameterless constructor + property setters — for a type with a parameterized
        //    constructor whose parameter count matches the query (a positional record like the removed
        //    LineCountRow), Dapper skips every registered type handler and instead requires a constructor
        //    matching the *raw* database column types directly (github.com/StackExchange/Dapper/issues/461).
        // 2. LineCount is a correlated-subquery expression column with no SQLite-declared type. Per
        //    Microsoft's own Sqlite type docs, System.Byte[] maps to SQLite BLOB — and an undeclared-type
        //    column takes BLOB affinity by default (confirmed via aspnet/Microsoft.Data.Sqlite#433), which
        //    is what a query with zero matching rows falls back to when Dapper has no row to sample a
        //    runtime type from. `CAST(... AS INTEGER)` forces a real INTEGER/Int64 result once a row
        //    exists, but does nothing to change the zero-row fallback.
        // Dynamic row access needs neither: it reads each value by name via IDictionary<string, object>
        // and converts explicitly below, with no constructor-matching or declared-type inference involved.
        var rows = await conn.QueryAsync(
            Sql.ConversationLines.SelectLineCountsForConversations, new { conversationIds });

        var result = new Dictionary<Guid, int>();
        foreach (var row in rows)
        {
            var dict = (IDictionary<string, object>)row;
            result[Guid.Parse((string)dict["ConversationId"])] = Convert.ToInt32(dict["LineCount"]);
        }
        return result;
    }
}
