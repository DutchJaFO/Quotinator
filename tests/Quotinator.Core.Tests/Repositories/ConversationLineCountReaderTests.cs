using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Core.Repositories;

namespace Quotinator.Core.Tests.Repositories;

/// <summary>
/// Real-SQLite tests for <see cref="ConversationLineCountReader"/> — a fake-backed unit test cannot catch
/// bugs in the reader's own SQL, since the fake never executes it. Two genuine bugs surfaced this way
/// during #189's live T2 pass, neither of which any fake-backed endpoint test could have caught:
/// (1) the original <c>SELECT DISTINCT id, (SELECT COUNT(*) ...) AS LineCount</c> shape left
/// <c>LineCount</c> with no declared SQLite column type, and Dapper's positional-record constructor
/// matching proved unreliable against it (observed as both <c>System.Byte[]</c> and <c>System.Int64</c>
/// across otherwise-identical query shapes) — fixed by reading rows dynamically instead of via a
/// positional record; (2) <c>cl.ConversationId IN @conversationIds</c> silently matched zero rows against
/// #68's curated-JSON conversations, whose ids were seeded lowercase (preserved verbatim from the import
/// file, per CLAUDE.md's case-insensitivity convention) while <c>GuidHandler</c> always uppercases the
/// bound parameters — fixed with <c>UPPER(cl.ConversationId) IN @conversationIds</c>.
/// </summary>
[TestClass]
public class ConversationLineCountReaderTests
{
    private string _tempDir = null!;
    private string _dbPath = null!;
    private SqliteConnectionFactory _factory = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_clcr_test_").FullName;
        _dbPath = Path.Combine(_tempDir, "test.db");
        _factory = new SqliteConnectionFactory(_dbPath);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE Conversations (
                Id TEXT PRIMARY KEY,
                Description TEXT,
                DateCreated TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE ConversationLines (
                Id TEXT PRIMARY KEY,
                ConversationId TEXT NOT NULL,
                [Order] INTEGER NOT NULL,
                DateCreated TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );
            """);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void InsertConversation(Guid id, bool lowercaseId = false) =>
        Execute("INSERT INTO Conversations (Id, DateCreated) VALUES (@id, '2026-01-01 00:00:00');",
            new { id = FormatId(id, lowercaseId) });

    private void InsertLine(Guid conversationId, int order, bool isDeleted = false, bool lowercaseId = false) =>
        Execute(
            "INSERT INTO ConversationLines (Id, ConversationId, [Order], DateCreated, IsDeleted) " +
            "VALUES (@id, @conversationId, @order, '2026-01-01 00:00:00', @isDeleted);",
            new
            {
                id = Guid.NewGuid().ToString("D").ToUpperInvariant(),
                conversationId = FormatId(conversationId, lowercaseId),
                order,
                isDeleted = isDeleted ? 1 : 0,
            });

    private static string FormatId(Guid id, bool lowercase) =>
        lowercase ? id.ToString("D").ToLowerInvariant() : id.ToString("D").ToUpperInvariant();

    private void Execute(string sql, object param)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute(sql, param);
    }

    [TestMethod]
    public async Task GetLineCountsForManyAsync_ConversationWithLines_DoesNotThrowAndReturnsCorrectCount()
    {
        var conversationId = Guid.NewGuid();
        InsertConversation(conversationId);
        InsertLine(conversationId, 1);
        InsertLine(conversationId, 2);
        InsertLine(conversationId, 3);

        var reader = new ConversationLineCountReader(_factory);
        var result = await reader.GetLineCountsForManyAsync([conversationId]);

        Assert.AreEqual(3, result[conversationId]);
    }

    [TestMethod]
    public async Task GetLineCountsForManyAsync_SoftDeletedLinesExcluded_NotCountedTowardsResult()
    {
        var conversationId = Guid.NewGuid();
        InsertConversation(conversationId);
        InsertLine(conversationId, 1);
        InsertLine(conversationId, 2, isDeleted: true);

        var reader = new ConversationLineCountReader(_factory);
        var result = await reader.GetLineCountsForManyAsync([conversationId]);

        Assert.AreEqual(1, result[conversationId]);
    }

    [TestMethod]
    public async Task GetLineCountsForManyAsync_ConversationWithNoLines_AbsentFromResult()
    {
        var conversationId = Guid.NewGuid();
        InsertConversation(conversationId);

        var reader = new ConversationLineCountReader(_factory);
        var result = await reader.GetLineCountsForManyAsync([conversationId]);

        Assert.IsFalse(result.ContainsKey(conversationId));
    }

    [TestMethod]
    public async Task GetLineCountsForManyAsync_MultipleConversations_EachCountedIndependently()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        InsertConversation(idA);
        InsertConversation(idB);
        InsertLine(idA, 1);
        InsertLine(idB, 1);
        InsertLine(idB, 2);

        var reader = new ConversationLineCountReader(_factory);
        var result = await reader.GetLineCountsForManyAsync([idA, idB]);

        Assert.AreEqual(1, result[idA]);
        Assert.AreEqual(2, result[idB]);
    }

    /// <summary>
    /// Reproduces #189's live T2 bug directly: #68's curated JSON conversations were seeded with their
    /// file-authored lowercase ids preserved verbatim, while the reader binds @conversationIds via
    /// GuidHandler, which always uppercases. An exact-case IN match against the unmodified column
    /// silently returned zero rows for every real conversation before UPPER() was added to the query.
    /// </summary>
    [TestMethod]
    public async Task GetLineCountsForManyAsync_ConversationIdStoredLowercase_StillMatchesUppercaseBoundParameter()
    {
        var conversationId = Guid.NewGuid();
        InsertConversation(conversationId, lowercaseId: true);
        InsertLine(conversationId, 1, lowercaseId: true);
        InsertLine(conversationId, 2, lowercaseId: true);

        var reader = new ConversationLineCountReader(_factory);
        var result = await reader.GetLineCountsForManyAsync([conversationId]);

        Assert.AreEqual(2, result[conversationId]);
    }
}
