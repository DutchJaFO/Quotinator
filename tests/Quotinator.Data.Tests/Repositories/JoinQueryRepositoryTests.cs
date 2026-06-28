using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.Database;
using Quotinator.Data.Testing.Fakes;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class JoinQueryRepositoryTests
{
    private TempDatabase _db = null!;

    private const string CreateOwners = """
        CREATE TABLE Owners (
            Id    TEXT NOT NULL PRIMARY KEY,
            Name  TEXT NOT NULL
        );
        """;

    private const string CreateWidgets = """
        CREATE TABLE Widgets (
            Id        TEXT    NOT NULL PRIMARY KEY,
            Label     TEXT    NOT NULL,
            OwnerId   TEXT    NOT NULL,
            IsDeleted INTEGER NOT NULL DEFAULT 0
        );
        """;

    [TestInitialize]
    public void Init() => _db = new TempDatabase([CreateOwners, CreateWidgets]);

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    [TestMethod]
    public async Task QueryAsync_ReturnsProjectedReadModels()
    {
        Seed("o1", "Alice", Guid.NewGuid().ToString(), "Widget A");

        var repo   = MakeRepo();
        var result = await repo.QueryAsync();

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public async Task QueryAsync_WidgetWithOwner_MapsAllColumns()
    {
        var widgetId = Guid.NewGuid();
        Seed(ownerId: "o2", ownerName: "Bob", widgetId: widgetId.ToString(), widgetLabel: "Widget B");

        var repo   = MakeRepo();
        var result = await repo.QueryAsync();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(widgetId,  result[0].WidgetId);
        Assert.AreEqual("Widget B", result[0].Label);
        Assert.AreEqual("Bob",      result[0].OwnerName);
    }

    [TestMethod]
    public async Task QueryAsync_LeftJoin_NullRightSide_ReturnedWithDefaults()
    {
        // Widget whose OwnerId has no matching Owners row — LEFT JOIN returns a row with a null/default OwnerName.
        // Uses FakeJoinStrategy to inject a LEFT JOIN variant that COALESCE-s null to empty string.
        const string leftJoinSql = """
            SELECT [w].[Id] AS WidgetId, [w].[Label],
                   COALESCE([o].[Name], '') AS OwnerName
            FROM   [Widgets] [w]
            LEFT JOIN [Owners] [o] ON [w].[OwnerId] = [o].[Id]
            WHERE  [w].[IsDeleted] = 0
            """;

        var widgetId  = Guid.NewGuid();
        var missingId = Guid.NewGuid().ToString();

        using var conn = new SqliteConnection($"Data Source={_db.DbPath}");
        conn.Open();
        // Insert widget with an OwnerId that has no matching row in Owners (no FK constraint in test schema).
        conn.Execute("INSERT INTO Widgets (Id, Label, OwnerId, IsDeleted) VALUES (@id, 'Orphan', @ownerId, 0);",
            new { id = widgetId.ToString(), ownerId = missingId });

        var strategy = new FakeJoinStrategy<WidgetWithOwner>(leftJoinSql);
        var repo     = new JoinQueryRepository<WidgetWithOwner>(_db.ConnectionFactory, strategy);
        var result   = await repo.QueryAsync();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(widgetId,     result[0].WidgetId);
        Assert.AreEqual(string.Empty, result[0].OwnerName);
    }

    private JoinQueryRepository<WidgetWithOwner> MakeRepo()
        => new(_db.ConnectionFactory, new WidgetWithOwnerStrategy());

    private void Seed(string ownerId, string ownerName, string widgetId, string widgetLabel)
    {
        using var conn = new SqliteConnection($"Data Source={_db.DbPath}");
        conn.Open();
        conn.Execute("INSERT INTO Owners (Id, Name) VALUES (@ownerId, @ownerName);",
            new { ownerId, ownerName });
        conn.Execute("INSERT INTO Widgets (Id, Label, OwnerId, IsDeleted) VALUES (@widgetId, @widgetLabel, @ownerId, 0);",
            new { widgetId, widgetLabel, ownerId });
    }
}
