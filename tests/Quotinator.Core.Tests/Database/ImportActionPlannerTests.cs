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
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Services;

namespace Quotinator.Core.Tests.Database;

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
        await conn.ExecuteAsync("INSERT INTO Characters (Id, Name, DateCreated) VALUES (@Id, 'Rick Blaine', @now)",
            new { Id = realCharacterId, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
        // #179: Character<->Source is many-to-many via CharacterSources, not a Characters.SourceId column.
        await conn.ExecuteAsync("INSERT INTO CharacterSources (Id, CharacterId, SourceId, DateCreated) VALUES (@Id, @CharacterId, @SourceId, @now)",
            new { Id = Guid.NewGuid(), CharacterId = realCharacterId, SourceId = realSourceId, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
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

    private static SourceEntry BuildSourceEntry(string? id, string title = "Casablanca", Core.Models.QuoteType type = Core.Models.QuoteType.Movie, string? date = "1942", string? seriesName = null) => new()
    {
        Id         = id,
        Title      = title,
        Type       = type,
        Date       = date,
        SeriesName = seriesName,
    };

    /// <summary>#180: an enrichment-shaped entry — no explicit id (matched by natural key), no date (not intended to be set), just the Series link.</summary>
    private static SourceEntry BuildEnrichmentEntry(string title = "Casablanca", Core.Models.QuoteType type = Core.Models.QuoteType.Movie, string? seriesName = "The Hobbit") => new()
    {
        Title      = title,
        Type       = type,
        SeriesName = seriesName,
    };

    private static async Task SeedExplicitSourceAsync(SqliteConnection conn, string id, string title = "Casablanca", string type = "Movie", string? date = "1942", string completenessStatus = "Incomplete", string? seriesId = null)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Sources (Id, Title, Type, Date, SeriesId, CompletenessStatus, DateCreated) VALUES (@Id, @Title, @Type, @Date, @SeriesId, @CompletenessStatus, @now)",
            new { Id = id, Title = title, Type = type, Date = date, SeriesId = seriesId, CompletenessStatus = completenessStatus, now });
    }

    // ── #180: PlanUniverseAsync / PlanSeriesAsync / Source.SeriesId ─────────────

    private static UniverseEntry BuildUniverseEntry(string name = "Middle Earth") => new() { Name = name };

    private static SeriesEntry BuildSeriesEntry(string name = "The Lord of the Rings", string? universeName = null) => new()
    {
        Name         = name,
        UniverseName = universeName,
    };

    private static async Task<string> SeedExistingSeriesAsync(SqliteConnection conn, string name = "The Lord of the Rings", string? universeId = null)
    {
        var id  = Guid.NewGuid().ToString("D").ToUpperInvariant();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Series (Id, Name, UniverseId, CompletenessStatus, DateCreated) VALUES (@Id, @Name, @UniverseId, 'Incomplete', @now)",
            new { Id = id, Name = name, UniverseId = universeId, now });
        return id;
    }

    [TestMethod]
    public async Task PlanUniverseAsync_NoMatchAtAll_StagesAddAction()
    {
        using var conn = await OpenConnectionAsync();

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            universe: [BuildUniverseEntry("Middle Earth")]);

        var universeAction = actions.Single(a => a.EntityType == "Universe");
        Assert.AreEqual(ImportActionKind.Add, universeAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, universeAction.Status.Parsed);
    }

    [TestMethod]
    public async Task PlanUniverseAsync_ExistingByName_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Universe (Id, Name, CompletenessStatus, DateCreated) VALUES (@Id, 'Middle Earth', 'Incomplete', @now)",
            new { Id = Guid.NewGuid().ToString("D").ToUpperInvariant(), now });

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            universe: [BuildUniverseEntry("Middle Earth")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Universe"), "Already exists by name — silently reused, no action staged");
    }

    [TestMethod]
    public async Task PlanSeriesAsync_NoMatchAtAll_StagesAddAction()
    {
        using var conn = await OpenConnectionAsync();

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            series: [BuildSeriesEntry("The Lord of the Rings")]);

        var seriesAction = actions.Single(a => a.EntityType == "Series");
        Assert.AreEqual(ImportActionKind.Add, seriesAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, seriesAction.Status.Parsed);
    }

    [TestMethod]
    public async Task PlanSeriesAsync_UniverseNameResolvesToSameBatchUniverseAdd_PayloadCarriesUniverseId()
    {
        using var conn = await OpenConnectionAsync();

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            series: [BuildSeriesEntry("The Lord of the Rings", universeName: "Middle Earth")],
            universe: [BuildUniverseEntry("Middle Earth")]);

        var universeAction = actions.Single(a => a.EntityType == "Universe");
        var seriesAction   = actions.Single(a => a.EntityType == "Series");
        var payload = System.Text.Json.JsonSerializer.Deserialize<SeriesActionPayload>(seriesAction.IncomingValue!)!;
        Assert.AreEqual(universeAction.EntityId, payload.UniverseId, "Series' Add payload must carry the same-batch Universe Add's own stable id");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_SeriesNameResolvesToSameBatchSeriesAdd_PayloadCarriesSeriesId()
    {
        using var conn = await OpenConnectionAsync();
        var sourceFileId = "c8111111-1111-4111-8111-111111111111";

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(sourceFileId, title: "A Brand New Film", seriesName: "The Lord of the Rings")],
            series: [BuildSeriesEntry("The Lord of the Rings")]);

        var seriesAction = actions.Single(a => a.EntityType == "Series");
        var sourceAction = actions.Single(a => a.EntityType == "Source");
        var payload = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayload>(sourceAction.IncomingValue!)!;
        Assert.AreEqual(seriesAction.EntityId, payload.SeriesId, "Source's Add payload must carry the same-batch Series Add's own stable id");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_SeriesNameChanged_StagesModifyAction()
    {
        using var conn = await OpenConnectionAsync();
        var id = "c9111111-1111-4111-8111-111111111111";
        var seriesId = await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", seriesId: null);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(id, title: "Casablanca", seriesName: "The Hobbit")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, sourceAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, sourceAction.Status.Parsed);
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayload>(sourceAction.MergedFields!)!;
        Assert.AreEqual(seriesId, merged.SeriesId);
    }

    // ── #180: enrichment-shaped sources[] entry (no explicit id, no date) ───────
    // A curated overlay file exists to set seriesName on Sources the quote files already created.
    // It must not have to author a generated id, and must not have to state a date it has no
    // intention of setting — so an entry omitting both is matched by natural key (title+type) and
    // stages a Modify diffing seriesId ONLY. Title/Type can't be corrections on this path (they ARE
    // the lookup key — that's exactly what #162's explicit id exists for), and Date is carried
    // through from the existing row unchanged on both sides of the diff, which is what encodes
    // "don't touch it" without the file needing to express absent-vs-null (see Notes).

    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_NaturalKeyMatch_SeriesNameSet_StagesModify()
    {
        using var conn = await OpenConnectionAsync();
        var seriesId = await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, "cc111111-1111-4111-8111-111111111111", title: "Casablanca", date: "1942", seriesId: null);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: "The Hobbit")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, sourceAction.ActionType.Parsed, "A natural-key match must stage a Modify, not be silently skipped");
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayload>(sourceAction.MergedFields!)!;
        Assert.AreEqual(seriesId, merged.SeriesId);
    }

    /// <summary>
    /// The core of #180's second design point: an entry that omits `date` must never reset the
    /// existing row's date. The resolved payload feeds Sql.Sources.UpdateFieldsById, which writes
    /// Date unconditionally — so a null here would silently wipe a real date on every apply.
    /// </summary>
    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_OmittedDate_PreservesExistingDate()
    {
        using var conn = await OpenConnectionAsync();
        await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, "cd111111-1111-4111-8111-111111111111", title: "Casablanca", date: "1942", seriesId: null);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: "The Hobbit")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayload>(sourceAction.MergedFields!)!;
        Assert.AreEqual("1942", merged.Date, "An omitted date must carry the existing row's value through, never null it out");
        Assert.AreEqual("Casablanca", merged.Title, "Title is the lookup key on this path — never a correction");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_NaturalKeyMatch_NoSeriesName_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        await SeedExplicitSourceAsync(conn, "ce111111-1111-4111-8111-111111111111", title: "Casablanca", date: "1942");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: null)]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source"), "Nothing to enrich and nothing to correct — unchanged from #162's own natural-key behaviour");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_AlreadyTagged_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var seriesId = await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, "cf111111-1111-4111-8111-111111111111", title: "Casablanca", seriesId: seriesId);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: "The Hobbit")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source"), "Already points at this Series — a true no-op, nothing staged even under Review");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_NoMatchAtAll_StagesAddWithComputedId()
    {
        using var conn = await OpenConnectionAsync();

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildEnrichmentEntry(title: "A Brand New Film", seriesName: null)]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Add, sourceAction.ActionType.Parsed);
        Assert.AreEqual(Quotinator.Core.Import.EntityIdentity.SourceId("A Brand New Film", "Movie"), sourceAction.EntityId,
            "With no explicit id in the file, an Add uses the EntityIdentity-derived stable id — the same one ResolveSourceAsync would compute for a quote referencing this title");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_CompleteStatus_SeriesNameSet_StagesBlocked()
    {
        using var conn = await OpenConnectionAsync();
        await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, "d0111111-1111-4111-8111-111111111111", title: "Casablanca", completenessStatus: "Complete", seriesId: null);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: "The Hobbit")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Blocked, sourceAction.Status.Parsed, "CompletenessGuard applies on the natural-key path too — a Complete row is never silently enriched");
    }

    /// <summary>#180 spec requirement 3: a genuine SeriesId disagreement under Review policy stages Pending, never silently resolves.</summary>
    [TestMethod]
    public async Task PlanSourcesAsync_ReviewPolicy_SeriesNameChanged_StagesPendingNotAutoResolved()
    {
        using var conn = await OpenConnectionAsync();
        var id = "cb111111-1111-4111-8111-111111111111";
        var originalSeriesId = await SeedExistingSeriesAsync(conn, "Original Series");
        await SeedExistingSeriesAsync(conn, "Edited Series");
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", seriesId: originalSeriesId);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            sources: [BuildSourceEntry(id, title: "Casablanca", seriesName: "Edited Series")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, sourceAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Pending, sourceAction.Status.Parsed, "A genuine SeriesId disagreement under review policy must stage Pending, not silently resolve");
        Assert.IsNull(sourceAction.MergedFields, "Nothing is resolved yet for a Pending action");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_CompleteStatus_SeriesNameChanged_StagesBlockedNotModify()
    {
        using var conn = await OpenConnectionAsync();
        var id = "ca111111-1111-4111-8111-111111111111";
        await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", completenessStatus: "Complete", seriesId: null);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(id, title: "Casablanca", seriesName: "The Hobbit")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Blocked, sourceAction.Status.Parsed, "A Complete row must never silently accept a Modify, including a SeriesId-only change");
        Assert.IsNull(sourceAction.MergedFields, "Nothing is resolved yet for a Blocked action");
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

    /// <summary>
    /// #180 case-sensitivity fix: a lowercase file-authored id must still match an existing row
    /// whose id was stored uppercase (the EntityIdentity convention). Before the fix, this case
    /// mismatch fell through to the natural-key fallback — which searches by the INCOMING title, not
    /// the existing row's — found nothing, and staged a phantom duplicate Add instead of the intended
    /// Modify.
    /// </summary>
    [TestMethod]
    public async Task PlanSourcesAsync_LowercaseFileId_MatchesUppercaseStoredId_StagesModifyNotDuplicateAdd()
    {
        using var conn = await OpenConnectionAsync();
        var uppercaseId = "CB111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(conn, uppercaseId, title: "Casablanca");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(uppercaseId.ToLowerInvariant(), title: "Casablanca (Corrected)")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, sourceAction.ActionType.Parsed, "Must match the existing row by id (case-insensitively), not stage a duplicate Add");
        Assert.AreEqual(ImportActionStatus.Decided, sourceAction.Status.Parsed);
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
        // #190: date must be passed explicitly as null here — BuildSourceEntry's own default ("1942")
        // would otherwise now genuinely take effect on the natural-key path (requirement 6's
        // liberalization), which is a different, separately-tested scenario, not what this test means
        // to exercise (nothing about this entry differs from the existing row at all).
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(newFileId, title: "Casablanca", type: Core.Models.QuoteType.Movie, date: null)]);

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

    // ── #173: PlanPeopleAsync ─────────────────────────────────────────────────

    private static PersonEntry BuildPersonEntry(string id, string name = "Ada Lovelace", string? dateOfBirth = "1815-12-10", string? dateOfDeath = "1852-11-27") => new()
    {
        Id          = id,
        Name        = name,
        DateOfBirth = dateOfBirth,
        DateOfDeath = dateOfDeath,
    };

    private static async Task SeedExplicitPersonAsync(SqliteConnection conn, string id, string name = "Ada Lovelace", string? dateOfBirth = "1815-12-10", string? dateOfDeath = "1852-11-27", string completenessStatus = "Incomplete")
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO People (Id, Name, DateOfBirth, DateOfDeath, CompletenessStatus, DateCreated) VALUES (@Id, @Name, @DateOfBirth, @DateOfDeath, @CompletenessStatus, @now)",
            new { Id = id, Name = name, DateOfBirth = dateOfBirth, DateOfDeath = dateOfDeath, CompletenessStatus = completenessStatus, now });
    }

    [TestMethod]
    public async Task PlanPeopleAsync_IdMatchFound_NameDiffers_StagesModifyAction()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e1111111-1111-4111-8111-111111111173";
        await SeedExplicitPersonAsync(conn, id, name: "Ada Lovelace");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(id, name: "Augusta Ada King, Countess of Lovelace")]);

        var action = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed);
        Assert.IsNotNull(action.MergedFields);
    }

    [TestMethod]
    public async Task PlanPeopleAsync_IdMatchFound_NothingChanged_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e2111111-1111-4111-8111-111111111173";
        await SeedExplicitPersonAsync(conn, id, name: "Ada Lovelace", dateOfBirth: "1815-12-10", dateOfDeath: "1852-11-27");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(id, name: "Ada Lovelace", dateOfBirth: "1815-12-10", dateOfDeath: "1852-11-27")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Person"), "Nothing differs — silent reuse, no action staged");
    }

    [TestMethod]
    public async Task PlanPeopleAsync_NoIdMatch_FallsBackToNaturalKey_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        // A pre-existing row found only by natural key (Name) — never declared an explicit id before.
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("INSERT INTO People (Id, Name, DateCreated) VALUES (@Id, 'Ada Lovelace', @now)",
            new { Id = Guid.NewGuid(), now });

        var newFileId = "e3111111-1111-4111-8111-111111111173";
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(newFileId, name: "Ada Lovelace")]);

        Assert.AreEqual(0, actions.Count, "Not-yet-migrated row found via natural key — no re-keying, nothing staged (#173 scope boundary, same as #162's)");
    }

    [TestMethod]
    public async Task PlanPeopleAsync_CompleteStatus_StagesBlockedNotModify()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e4111111-1111-4111-8111-111111111173";
        await SeedExplicitPersonAsync(conn, id, name: "Ada Lovelace", completenessStatus: "Complete");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(id, name: "A completely different name")]);

        var action = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(ImportActionStatus.Blocked, action.Status.Parsed, "A Complete row must never silently accept a Modify");
        Assert.IsNull(action.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanPeopleAsync_CompleteStatus_SkipPolicy_DoesNotBlock()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e5111111-1111-4111-8111-111111111173";
        await SeedExplicitPersonAsync(conn, id, name: "Ada Lovelace", completenessStatus: "Complete");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Skip,
            people: [BuildPersonEntry(id, name: "A completely different name")]);

        var action = actions.Single(a => a.EntityType == "Person");
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

    // ── #190: absent vs. explicit-null distinguishability ────────────────────

    [TestMethod]
    public async Task PlanSourcesAsync_ExplicitId_DateAbsent_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e0111111-1111-4111-8111-111111111181";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", date: "1942");

        var entry = new SourceEntry { Id = id, Title = "Casablanca", Type = Core.Models.QuoteType.Movie };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [entry]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source"), "An omitted 'date' must never be treated as a change, under any policy");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_ExplicitId_DateExplicitlyNull_StagesModifyResettingDate()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e0111111-1111-4111-8111-111111111182";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", date: "1942");

        var entry = new SourceEntry { Id = id, Title = "Casablanca", Type = Core.Models.QuoteType.Movie, Date = Optional<string>.Of(null) };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [entry]);

        var action = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed, "An explicit 'date: null' must resolve to a genuine reset");
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayload>(action.MergedFields!)!;
        Assert.IsNull(merged.Date);
    }

    [TestMethod]
    public async Task PlanSourcesAsync_ExplicitId_SeriesNameAbsent_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var seriesId = await SeedExistingSeriesAsync(conn, "The Hobbit");
        var id = "e0111111-1111-4111-8111-111111111183";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", date: "1942", seriesId: seriesId);

        var entry = new SourceEntry { Id = id, Title = "Casablanca", Type = Core.Models.QuoteType.Movie, Date = "1942" };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [entry]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source"), "An omitted 'seriesName' must never be treated as a change, under any policy — same bug as Date, found on the same DTO one field over (#190 scope-expansion finding)");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_ExplicitId_SeriesNameExplicitlyNull_StagesModifyClearingSeries()
    {
        using var conn = await OpenConnectionAsync();
        var seriesId = await SeedExistingSeriesAsync(conn, "The Hobbit");
        var id = "e0111111-1111-4111-8111-111111111184";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", date: "1942", seriesId: seriesId);

        var entry = new SourceEntry { Id = id, Title = "Casablanca", Type = Core.Models.QuoteType.Movie, Date = "1942", SeriesName = Optional<string>.Of(null) };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [entry]);

        var action = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed, "An explicit 'seriesName: null' must resolve to a genuine clear");
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayload>(action.MergedFields!)!;
        Assert.IsNull(merged.SeriesId);
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NaturalKey_DateExplicitlySet_NowTakesEffect()
    {
        using var conn = await OpenConnectionAsync();
        // Not referenced by the entry below — a row found only by natural key (Title+Type).
        await SeedExplicitSourceAsync(conn, "e0111111-1111-4111-8111-111111111185", title: "Casablanca", date: null);

        // #180's enrichment shape: no explicit id.
        var entry = new SourceEntry { Title = "Casablanca", Type = Core.Models.QuoteType.Movie, Date = "1975" };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [entry]);

        var action = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed,
            "#190 requirement 6's liberalization: a natural-key entry that explicitly sets 'date' now actually takes effect, where it was previously always silently ignored regardless of what the file said");
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayload>(action.MergedFields!)!;
        Assert.AreEqual("1975", merged.Date);
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NaturalKey_MergeOurs_ExistingSeriesWins()
    {
        using var conn = await OpenConnectionAsync();
        var originalSeriesId = await SeedExistingSeriesAsync(conn, "Original Series");
        await SeedExistingSeriesAsync(conn, "New Series");
        // Not referenced by the entry below — a row found only by natural key (Title+Type).
        await SeedExplicitSourceAsync(conn, "e0111111-1111-4111-8111-111111111186", title: "Casablanca", seriesId: originalSeriesId);

        var entry = new SourceEntry { Title = "Casablanca", Type = Core.Models.QuoteType.Movie, SeriesName = "New Series" };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.MergeOurs,
            sources: [entry]);

        var action = actions.Single(a => a.EntityType == "Source");
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayload>(action.MergedFields!)!;
        Assert.AreEqual(originalSeriesId, merged.SeriesId,
            "#190 drive-by fix: MergeOurs must keep the existing Series on a genuine conflict — this branch previously never consulted FieldMergeResolver at all and always took the incoming value unconditionally");
    }

    [TestMethod]
    public async Task PlanPeopleAsync_DateOfBirthAbsent_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e0111111-1111-4111-8111-111111111187";
        await SeedExplicitPersonAsync(conn, id, name: "Ada Lovelace", dateOfBirth: "1815-12-10", dateOfDeath: "1852-11-27");

        var entry = new PersonEntry { Id = id, Name = "Ada Lovelace", DateOfDeath = "1852-11-27" };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [entry]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Person"), "An omitted 'dateOfBirth' must never be treated as a change, under any policy");
    }

    [TestMethod]
    public async Task PlanPeopleAsync_DateOfDeathExplicitlyNull_StagesModifyResettingDate()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e0111111-1111-4111-8111-111111111188";
        await SeedExplicitPersonAsync(conn, id, name: "Ada Lovelace", dateOfBirth: "1815-12-10", dateOfDeath: "1852-11-27");

        var entry = new PersonEntry { Id = id, Name = "Ada Lovelace", DateOfBirth = "1815-12-10", DateOfDeath = Optional<string>.Of(null) };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [entry]);

        var action = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed, "An explicit 'dateOfDeath: null' must resolve to a genuine reset");
        var merged = System.Text.Json.JsonSerializer.Deserialize<PersonActionPayload>(action.MergedFields!)!;
        Assert.IsNull(merged.DateOfDeath);
    }

    [TestMethod]
    public async Task PlanStageDirectionsAsync_ImageUrlAbsent_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e0111111-1111-4111-8111-111111111189";
        await SeedExplicitStageDirectionAsync(conn, id, text: "A shot rings out.", imageUrl: "http://example.com/still.jpg");

        var entry = new SourceStageDirection { Id = id, Text = "A shot rings out." };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            stageDirections: [entry]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "StageDirection"), "An omitted 'imageUrl' must never be treated as a change, under any policy — must preserve a real existing value, not just null-matches-null");
    }

    [TestMethod]
    public async Task PlanSoundCuesAsync_SoundFileUrlAbsent_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e0111111-1111-4111-8111-111111111191";
        await SeedExplicitSoundCueAsync(conn, id, text: "Distant thunder.", soundFileUrl: "http://example.com/thunder.mp3", imageUrl: "http://example.com/img.jpg");

        var entry = new SourceSoundCue { Id = id, Text = "Distant thunder.", ImageUrl = "http://example.com/img.jpg" };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            soundCues: [entry]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "SoundCue"), "An omitted 'soundFileUrl' must never be treated as a change, under any policy — must preserve a real existing value, not just null-matches-null");
    }

    [TestMethod]
    public async Task PlanConversationsAsync_DescriptionAbsent_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e0111111-1111-4111-8111-111111111192";
        await SeedExplicitConversationAsync(conn, id, description: "A tense standoff.");

        var entry = new SourceConversation { Id = id, Lines = [] };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            conversations: [entry]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Conversation"), "An omitted 'description' must never be treated as a change, under any policy");
    }
}
