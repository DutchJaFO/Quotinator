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

    private static SourceQuote BuildQuote(string id, string source = "Casablanca", string? character = "Rick Blaine", string? author = null, string quoteText = "Here's looking at you, kid.") => new()
    {
        Id               = id,
        QuoteText        = quoteText,
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
    public async Task PlanAsync_QuoteAlreadyComplete_ChangedFields_StagesBlockedNotModify()
    {
        using var conn = await OpenConnectionAsync();
        var id = "41211111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id, completenessStatus: "Complete");

        var quote = BuildQuote(id, source: "Casablanca", quoteText: "A different line entirely.");
        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Blocked, quoteAction.Status.Parsed, "A Complete quote must never silently accept a Modify");
        Assert.IsNull(quoteAction.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanAsync_QuoteAlreadyComplete_SkipPolicy_DoesNotBlock()
    {
        using var conn = await OpenConnectionAsync();
        var id = "41311111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id, completenessStatus: "Complete");

        var quote = BuildQuote(id, source: "Casablanca", quoteText: "A different line entirely.");
        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Skip);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed, "Skip's resolved value always equals the existing row — nothing would change, so a Complete row must never block");
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

    private static async Task SeedExistingQuoteAsync(SqliteConnection conn, string id, string completenessStatus = "Incomplete")
    {
        var now      = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var sourceId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO Sources (Id, Title, Type, DateCreated) VALUES (@Id, 'Casablanca', 'Movie', @now)", new { Id = sourceId, now });
        await conn.ExecuteAsync(
            "INSERT INTO Quotes (Id, QuoteText, OriginalLanguage, SourceId, CompletenessStatus, DateCreated) VALUES (@Id, 'Original text', 'en', @SourceId, @CompletenessStatus, @now)",
            new { Id = id, SourceId = sourceId, CompletenessStatus = completenessStatus, now });
    }

    // ── #162: PlanSourcesAsync ────────────────────────────────────────────────

    private static SourceEntry BuildSourceEntry(string id, string title = "Casablanca", Core.Models.QuoteType type = Core.Models.QuoteType.Movie, string? date = "1942") => new()
    {
        Id    = id,
        Title = title,
        Type  = type,
        Date  = date,
    };

    private static async Task SeedExplicitSourceAsync(SqliteConnection conn, string id, string title = "Casablanca", string type = "Movie", string? date = "1942", string completenessStatus = "Incomplete")
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Sources (Id, Title, Type, Date, CompletenessStatus, DateCreated) VALUES (@Id, @Title, @Type, @Date, @CompletenessStatus, @now)",
            new { Id = id, Title = title, Type = type, Date = date, CompletenessStatus = completenessStatus, now });
    }

    [TestMethod]
    public async Task PlanSourcesAsync_IdMatchFound_TitleDiffers_StagesModifyAction()
    {
        using var conn = await OpenConnectionAsync();
        var id = "c1111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(id, title: "Casablanca (1942)")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, sourceAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, sourceAction.Status.Parsed);
        Assert.IsNotNull(sourceAction.MergedFields);
    }

    [TestMethod]
    public async Task PlanSourcesAsync_IdMatchFound_NothingChanged_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var id = "c2111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", type: "Movie", date: "1942");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(id, title: "Casablanca", type: Core.Models.QuoteType.Movie, date: "1942")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source"), "Nothing differs — silent reuse, no action staged");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoIdMatch_FallsBackToNaturalKey_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        // A pre-existing row found only by natural key (Title+Type) — never declared an explicit id before.
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("INSERT INTO Sources (Id, Title, Type, DateCreated) VALUES (@Id, 'Casablanca', 'Movie', @now)",
            new { Id = Guid.NewGuid(), now });

        var newFileId = "c3111111-1111-4111-8111-111111111111";
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(newFileId, title: "Casablanca", type: Core.Models.QuoteType.Movie)]);

        Assert.AreEqual(0, actions.Count, "Not-yet-migrated row found via natural key — no re-keying, nothing staged (#162 scope boundary)");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoMatchAtAll_StagesAddWithFileId()
    {
        using var conn = await OpenConnectionAsync();
        var newFileId = "c4111111-1111-4111-8111-111111111111";

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(newFileId, title: "A Brand New Film")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Add, sourceAction.ActionType.Parsed);
        Assert.AreEqual(newFileId, sourceAction.EntityId, "Add uses the file's own declared id, not an EntityIdentity-derived stable id");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_CompleteStatus_StagesBlockedNotModify()
    {
        using var conn = await OpenConnectionAsync();
        var id = "c5111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", completenessStatus: "Complete");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(id, title: "Casablanca (Corrected)")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Blocked, sourceAction.Status.Parsed, "A Complete row must never silently accept a Modify");
        Assert.IsNull(sourceAction.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_CompleteSource_SkipPolicy_DoesNotBlock()
    {
        using var conn = await OpenConnectionAsync();
        var id = "c5211111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", completenessStatus: "Complete");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Skip,
            sources: [BuildSourceEntry(id, title: "Casablanca (Corrected)")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Decided, sourceAction.Status.Parsed, "Skip's resolved value always equals the existing row — nothing would change, so a Complete row must never block");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_QuoteReferencesExplicitlyDeclaredSource_ResolvesToItsId()
    {
        using var conn = await OpenConnectionAsync();
        var newFileId = "c6111111-1111-4111-8111-111111111111";
        var quote = BuildQuote("c7111111-1111-4111-8111-111111111111", source: "A Brand New Film");

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(newFileId, title: "A Brand New Film")]);

        Assert.AreEqual(1, actions.Count(a => a.EntityType == "Source"), "Only one Source Add — the quote must resolve to the same row the sources[] section staged, not a second one");
        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayload>(quoteAction.IncomingValue!)!;
        Assert.AreEqual(newFileId, payload.SourceId);
    }

    // ── #171: PlanStageDirectionsAsync ───────────────────────────────────────

    private static SourceStageDirection BuildStageDirectionEntry(string id, string text = "A shot rings out.", string? imageUrl = null) => new()
    {
        Id       = id,
        Text     = text,
        ImageUrl = imageUrl,
    };

    private static async Task SeedExplicitStageDirectionAsync(SqliteConnection conn, string id, string text = "A shot rings out.", string? imageUrl = null, string completenessStatus = "Incomplete")
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO StageDirections (Id, Text, ImageUrl, CompletenessStatus, DateCreated) VALUES (@Id, @Text, @ImageUrl, @CompletenessStatus, @now)",
            new { Id = id, Text = text, ImageUrl = imageUrl, CompletenessStatus = completenessStatus, now });
    }

    [TestMethod]
    public async Task PlanStageDirectionsAsync_IdMatchFound_TextDiffers_StagesModifyAction()
    {
        using var conn = await OpenConnectionAsync();
        var id = "d1111111-1111-4111-8111-111111111111";
        await SeedExplicitStageDirectionAsync(conn, id, text: "A shot rings out.");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            stageDirections: [BuildStageDirectionEntry(id, text: "A single shot rings out in the distance.")]);

        var action = actions.Single(a => a.EntityType == "StageDirection");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed);
        Assert.IsNotNull(action.MergedFields);
    }

    [TestMethod]
    public async Task PlanStageDirectionsAsync_IdMatchFound_NothingChanged_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var id = "d2111111-1111-4111-8111-111111111111";
        await SeedExplicitStageDirectionAsync(conn, id, text: "A shot rings out.", imageUrl: "https://example.com/still.jpg");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            stageDirections: [BuildStageDirectionEntry(id, text: "A shot rings out.", imageUrl: "https://example.com/still.jpg")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "StageDirection"), "Nothing differs — silent reuse, no action staged");
    }

    [TestMethod]
    public async Task PlanStageDirectionsAsync_CompleteStatus_StagesBlockedNotModify()
    {
        using var conn = await OpenConnectionAsync();
        var id = "d3111111-1111-4111-8111-111111111111";
        await SeedExplicitStageDirectionAsync(conn, id, text: "A shot rings out.", completenessStatus: "Complete");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            stageDirections: [BuildStageDirectionEntry(id, text: "A different action entirely.")]);

        var action = actions.Single(a => a.EntityType == "StageDirection");
        Assert.AreEqual(ImportActionStatus.Blocked, action.Status.Parsed, "A Complete row must never silently accept a Modify");
        Assert.IsNull(action.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanStageDirectionsAsync_CompleteStatus_SkipPolicy_DoesNotBlock()
    {
        using var conn = await OpenConnectionAsync();
        var id = "d4111111-1111-4111-8111-111111111111";
        await SeedExplicitStageDirectionAsync(conn, id, text: "A shot rings out.", completenessStatus: "Complete");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Skip,
            stageDirections: [BuildStageDirectionEntry(id, text: "A different action entirely.")]);

        var action = actions.Single(a => a.EntityType == "StageDirection");
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed, "Skip's resolved value always equals the existing row — nothing would change, so a Complete row must never block");
    }

    // ── #172: PlanSoundCuesAsync ──────────────────────────────────────────────

    private static SourceSoundCue BuildSoundCueEntry(string id, string text = "Distant thunder.", string? soundFileUrl = null, string? imageUrl = null) => new()
    {
        Id           = id,
        Text         = text,
        SoundFileUrl = soundFileUrl,
        ImageUrl     = imageUrl,
    };

    private static async Task SeedExplicitSoundCueAsync(SqliteConnection conn, string id, string text = "Distant thunder.", string? soundFileUrl = null, string? imageUrl = null, string completenessStatus = "Incomplete")
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO SoundCues (Id, Text, SoundFileUrl, ImageUrl, CompletenessStatus, DateCreated) VALUES (@Id, @Text, @SoundFileUrl, @ImageUrl, @CompletenessStatus, @now)",
            new { Id = id, Text = text, SoundFileUrl = soundFileUrl, ImageUrl = imageUrl, CompletenessStatus = completenessStatus, now });
    }

    [TestMethod]
    public async Task PlanSoundCuesAsync_IdMatchFound_TextDiffers_StagesModifyAction()
    {
        using var conn = await OpenConnectionAsync();
        var id = "d5111111-1111-4111-8111-111111111111";
        await SeedExplicitSoundCueAsync(conn, id, text: "Distant thunder.");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            soundCues: [BuildSoundCueEntry(id, text: "Rolling thunder in the distance.")]);

        var action = actions.Single(a => a.EntityType == "SoundCue");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed);
        Assert.IsNotNull(action.MergedFields);
    }

    [TestMethod]
    public async Task PlanSoundCuesAsync_IdMatchFound_NothingChanged_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var id = "d6111111-1111-4111-8111-111111111111";
        await SeedExplicitSoundCueAsync(conn, id, text: "Distant thunder.", soundFileUrl: "https://example.com/thunder.mp3");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            soundCues: [BuildSoundCueEntry(id, text: "Distant thunder.", soundFileUrl: "https://example.com/thunder.mp3")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "SoundCue"), "Nothing differs — silent reuse, no action staged");
    }

    [TestMethod]
    public async Task PlanSoundCuesAsync_CompleteStatus_StagesBlockedNotModify()
    {
        using var conn = await OpenConnectionAsync();
        var id = "d7111111-1111-4111-8111-111111111111";
        await SeedExplicitSoundCueAsync(conn, id, text: "Distant thunder.", completenessStatus: "Complete");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            soundCues: [BuildSoundCueEntry(id, text: "A completely different sound.")]);

        var action = actions.Single(a => a.EntityType == "SoundCue");
        Assert.AreEqual(ImportActionStatus.Blocked, action.Status.Parsed, "A Complete row must never silently accept a Modify");
        Assert.IsNull(action.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanSoundCuesAsync_CompleteStatus_SkipPolicy_DoesNotBlock()
    {
        using var conn = await OpenConnectionAsync();
        var id = "d8111111-1111-4111-8111-111111111111";
        await SeedExplicitSoundCueAsync(conn, id, text: "Distant thunder.", completenessStatus: "Complete");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Skip,
            soundCues: [BuildSoundCueEntry(id, text: "A completely different sound.")]);

        var action = actions.Single(a => a.EntityType == "SoundCue");
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed, "Skip's resolved value always equals the existing row — nothing would change, so a Complete row must never block");
    }

    // ── #176: PlanConversationsAsync ─────────────────────────────────────────

    private static SourceConversation BuildConversationEntry(string id, string? description = "A tense standoff.") => new()
    {
        Id          = id,
        Description = description,
        Lines       = [],
    };

    private static async Task SeedExplicitConversationAsync(SqliteConnection conn, string id, string? description = "A tense standoff.", string completenessStatus = "Incomplete")
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Conversations (Id, Description, CompletenessStatus, DateCreated) VALUES (@Id, @Description, @CompletenessStatus, @now)",
            new { Id = id, Description = description, CompletenessStatus = completenessStatus, now });
    }

    [TestMethod]
    public async Task PlanConversationsAsync_IdMatchFound_DescriptionDiffers_StagesModifyAction()
    {
        using var conn = await OpenConnectionAsync();
        var id = "d9111111-1111-4111-8111-111111111176";
        await SeedExplicitConversationAsync(conn, id, description: "A tense standoff.");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            conversations: [BuildConversationEntry(id, description: "A tense standoff in the saloon.")]);

        var action = actions.Single(a => a.EntityType == "Conversation");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed);
        Assert.IsNotNull(action.MergedFields);
    }

    [TestMethod]
    public async Task PlanConversationsAsync_IdMatchFound_NothingChanged_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var id = "da111111-1111-4111-8111-111111111176";
        await SeedExplicitConversationAsync(conn, id, description: "A tense standoff.");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            conversations: [BuildConversationEntry(id, description: "A tense standoff.")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Conversation"), "Nothing differs — silent reuse, no action staged");
    }

    [TestMethod]
    public async Task PlanConversationsAsync_IdMatchFound_LinesNeverDiffed()
    {
        using var conn = await OpenConnectionAsync();
        var id = "db111111-1111-4111-8111-111111111176";
        await SeedExplicitConversationAsync(conn, id, description: "A tense standoff.");

        var entry = new SourceConversation
        {
            Id          = id,
            Description = "A tense standoff in the saloon.",
            Lines       = [new SourceConversationLine { Order = 0, Type = Core.Models.ConversationLineType.Quote, QuoteId = "11111111-1111-4111-8111-111111111111" }],
        };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, conversations: [entry]);

        var action = actions.Single(a => a.EntityType == "Conversation");
        var merged = System.Text.Json.JsonSerializer.Deserialize<ConversationActionPayload>(action.MergedFields!)!;
        Assert.AreEqual(0, merged.Lines.Count, "Lines are never read or included in a Modify payload — out of scope for this issue");
    }

    [TestMethod]
    public async Task PlanConversationsAsync_CompleteStatus_StagesBlockedNotModify()
    {
        using var conn = await OpenConnectionAsync();
        var id = "dc111111-1111-4111-8111-111111111176";
        await SeedExplicitConversationAsync(conn, id, description: "A tense standoff.", completenessStatus: "Complete");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            conversations: [BuildConversationEntry(id, description: "A completely different scene.")]);

        var action = actions.Single(a => a.EntityType == "Conversation");
        Assert.AreEqual(ImportActionStatus.Blocked, action.Status.Parsed, "A Complete row must never silently accept a Modify");
        Assert.IsNull(action.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanConversationsAsync_CompleteStatus_SkipPolicy_DoesNotBlock()
    {
        using var conn = await OpenConnectionAsync();
        var id = "dd111111-1111-4111-8111-111111111176";
        await SeedExplicitConversationAsync(conn, id, description: "A tense standoff.", completenessStatus: "Complete");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Skip,
            conversations: [BuildConversationEntry(id, description: "A completely different scene.")]);

        var action = actions.Single(a => a.EntityType == "Conversation");
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed, "Skip's resolved value always equals the existing row — nothing would change, so a Complete row must never block");
    }
}
