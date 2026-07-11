using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Import;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Database;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Repositories;
using Quotinator.Engine.Services;

namespace Quotinator.Engine.Tests.Database;

/// <summary>
/// Exercises <see cref="ImportActionPlanner.PlanAsync"/> against a real, freshly-migrated SQLite
/// schema (no domain rows unless a test seeds them directly) — proves the planner is genuinely
/// read-only and classifies correctly, independent of the applier/coordinator that will later
/// consume its output.
/// </summary>
[TestClass]
public class ImportActionPlannerTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private SqliteConnectionFactory _factory = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_planner_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
        _factory = new SqliteConnectionFactory(_dbPath);

        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = Path.Combine(_tempDir, "backups") };
        var importBatches = new SqliteImportBatchRepository(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var actionReader  = new SystemImportActionReader(_factory);
        var actionWriter  = new SystemImportActionWriter(_factory);
        var coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, _factory);
        var actionService = new SqliteImportActionService(actionReader, coordinator, NoOpSystemChangeLogWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Source>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Character>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Person>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            importBatches, _factory);
        var db = new QuotinatorDatabaseInitializer(_factory, options, QuotinatorMigrations.All, [], importBatches,
            coordinator, actionService, NoOpSystemAuditWriter.Instance,
            NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance, NoOpSourceCacheUpdater.Instance,
            autoUpdateSources: false, QuotinatorMigrations.Baseline);
        await db.InitialiseAsync();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static SourceQuote BuildQuote(string id, string source = "Casablanca", string? character = "Rick Blaine", string? author = null) => new()
    {
        Id               = id,
        QuoteText        = "Here's looking at you, kid.",
        OriginalLanguage = "en",
        Source           = source,
        Character        = character,
        Author           = author,
        Type             = Core.Models.QuoteType.Movie,
    };

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        return conn;
    }

    [TestMethod]
    public async Task PlanAsync_BrandNewQuote_StagesAddActionsForQuoteSourceCharacterPerson()
    {
        using var conn = await OpenConnectionAsync();
        var quote = BuildQuote("11111111-1111-4111-8111-111111111111", author: "Someone");

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.AreEqual(4, actions.Count, "Quote + Source + Character + Person, all brand new");
        Assert.IsTrue(actions.All(a => a.ActionType.Parsed == ImportActionKind.Add));
        Assert.IsTrue(actions.All(a => a.Status.Parsed == ImportActionStatus.Decided), "Add is never ambiguous");

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(quote.Id, quoteAction.EntityId);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(EntityIdentity.SourceId("Casablanca", "Movie"), sourceAction.EntityId);

        var characterAction = actions.Single(a => a.EntityType == "Character");
        Assert.AreEqual(EntityIdentity.CharacterId(sourceAction.EntityId, "Rick Blaine"), characterAction.EntityId);

        var personAction = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(EntityIdentity.PersonId("Someone"), personAction.EntityId);
    }

    [TestMethod]
    public async Task PlanAsync_NoCharacterOrAuthor_StagesOnlyQuoteAndSourceActions()
    {
        using var conn = await OpenConnectionAsync();
        var quote = BuildQuote("21111111-1111-4111-8111-111111111111", character: null);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.AreEqual(2, actions.Count);
        CollectionAssert.AreEquivalent(new[] { "Quote", "Source" }, actions.Select(a => a.EntityType).ToList());
    }

    [TestMethod]
    public async Task PlanAsync_ExistingSourceCharacterPerson_ReusesRealIds_NoAddActionsForThem()
    {
        using var conn = await OpenConnectionAsync();

        var realSourceId    = Guid.NewGuid();
        var realCharacterId = Guid.NewGuid();
        var realPersonId    = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO Sources (Id, Title, Type, DateCreated) VALUES (@Id, 'Casablanca', 'Movie', @now)",
            new { Id = realSourceId, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
        await conn.ExecuteAsync("INSERT INTO Characters (Id, SourceId, Name, DateCreated) VALUES (@Id, @SourceId, 'Rick Blaine', @now)",
            new { Id = realCharacterId, SourceId = realSourceId, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
        await conn.ExecuteAsync("INSERT INTO People (Id, Name, DateCreated) VALUES (@Id, 'Someone', @now)",
            new { Id = realPersonId, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });

        var quote = BuildQuote("31111111-1111-4111-8111-111111111111", author: "Someone");
        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.AreEqual(1, actions.Count, "Only the Quote is new — Source/Character/Person all already exist");
        var quoteAction = actions.Single();
        Assert.AreEqual("Quote", quoteAction.EntityType);

        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayload>(quoteAction.IncomingValue!)!;
        // GuidHandler stores/reads Guid columns as uppercase "D"-format TEXT (see GuidHandler.cs) —
        // the resolved id must match that convention, not Guid.ToString()'s default lowercase form.
        Assert.AreEqual(realSourceId.ToString("D").ToUpperInvariant(), payload.SourceId, "Must resolve to the real existing Source id, not a stable id");
        Assert.AreEqual(realCharacterId.ToString("D").ToUpperInvariant(), payload.CharacterId);
        Assert.AreEqual(realPersonId.ToString("D").ToUpperInvariant(), payload.PersonId);
    }

    [TestMethod]
    public async Task PlanAsync_ExistingQuote_ReviewPolicy_StagesPendingModifyActionWithNoMergedFields()
    {
        using var conn = await OpenConnectionAsync();
        var id = "41111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id);

        var actions = await ImportActionPlanner.PlanAsync(conn, [BuildQuote(id, source: "Casablanca")], Guid.NewGuid(), DuplicateResolutionPolicy.Review);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionKind.Modify, quoteAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Pending, quoteAction.Status.Parsed);
        Assert.IsNull(quoteAction.MergedFields, "Pending actions have no resolved values yet");
        Assert.IsNotNull(quoteAction.ExistingValue);
    }

    [TestMethod]
    public async Task PlanAsync_ExistingQuote_NewestWinsPolicy_StagesDecidedModifyActionWithResolvedMergedFields()
    {
        using var conn = await OpenConnectionAsync();
        var id = "51111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id);

        var actions = await ImportActionPlanner.PlanAsync(conn, [BuildQuote(id, source: "Casablanca")], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionKind.Modify, quoteAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed);
        Assert.IsNotNull(quoteAction.MergedFields, "Non-Review policies resolve immediately at staging time");
    }

    [TestMethod]
    public async Task PlanAsync_TwoQuotesInSameBatchReferencingSameNewSource_StagesOnlyOneSourceAddAction()
    {
        using var conn = await OpenConnectionAsync();
        var q1 = BuildQuote("61111111-1111-4111-8111-111111111111", character: "Rick Blaine");
        var q2 = BuildQuote("71111111-1111-4111-8111-111111111111", character: "Ilsa Lund");

        var actions = await ImportActionPlanner.PlanAsync(conn, [q1, q2], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.AreEqual(1, actions.Count(a => a.EntityType == "Source"), "Both quotes share the same Source — must be staged once, not twice");
    }

    [TestMethod]
    public async Task PlanAsync_NeverWritesToAnyDomainTable()
    {
        using var conn = await OpenConnectionAsync();
        var quote = BuildQuote("81111111-1111-4111-8111-111111111111", author: "Someone");

        await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes"));
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sources"));
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Characters"));
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM People"));
    }

    [TestMethod]
    public async Task PlanAsync_CalledTwiceForSameNewSource_ProducesTheSameStableIdBothTimes()
    {
        using var conn1 = await OpenConnectionAsync();
        var actions1 = await ImportActionPlanner.PlanAsync(conn1, [BuildQuote("91111111-1111-4111-8111-111111111111")], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        using var conn2 = await OpenConnectionAsync();
        var actions2 = await ImportActionPlanner.PlanAsync(conn2, [BuildQuote("a1111111-1111-4111-8111-111111111111")], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        var sourceId1 = actions1.Single(a => a.EntityType == "Source").EntityId;
        var sourceId2 = actions2.Single(a => a.EntityType == "Source").EntityId;
        Assert.AreEqual(sourceId1, sourceId2, "Same title+type must always produce the same stable id, across independent PlanAsync calls");
    }

    private static async Task SeedExistingQuoteAsync(SqliteConnection conn, string id)
    {
        var now      = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var sourceId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO Sources (Id, Title, Type, DateCreated) VALUES (@Id, 'Casablanca', 'Movie', @now)", new { Id = sourceId, now });
        await conn.ExecuteAsync(
            "INSERT INTO Quotes (Id, QuoteText, OriginalLanguage, SourceId, DateCreated) VALUES (@Id, 'Original text', 'en', @SourceId, @now)",
            new { Id = id, SourceId = sourceId, now });
    }
}
