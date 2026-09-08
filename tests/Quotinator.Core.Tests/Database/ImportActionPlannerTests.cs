using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Enums;
using Quotinator.Core.Import;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Helpers;
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

        DatabaseOptions options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = Path.Combine(_tempDir, "backups") };
        SqliteImportBatchRepository importBatches = new SqliteImportBatchRepository(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        ImportActionReader actionReader  = new ImportActionReader(_factory);
        ImportActionWriter actionWriter  = new ImportActionWriter(_factory);
        ImportActionResolutionCoordinator coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, _factory);
        SqliteImportActionService actionService = new SqliteImportActionService(actionReader, coordinator, actionWriter, NoOpAuditEntryWriter.Instance, NoOpChangeWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SourceEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<CharacterEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<PersonEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            importBatches, _factory, NoOpNotificationWriter.Instance);
        QuotinatorDatabaseInitializer db = new QuotinatorDatabaseInitializer(_factory, options, QuotinatorMigrations.All, [], importBatches,
            coordinator, actionService, actionWriter, NoOpAuditEntryWriter.Instance,
            NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance, NoOpSourceCacheUpdater.Instance,
            autoUpdateSources: false,
            autoPurgeBundledImportActions: false, autoPurgeUserImportActions: false,
            NoOpRuleFileOverridePathResolver.Instance, NoOpSourceFileOverrideRegistry.Instance, NoOpFileResourceRepository.Instance,
            NoOpNotificationReader.Instance, NoOpNotificationWriter.Instance, NoOpNotificationTextSource.Instance,
            new AppVersionTracker(_factory), new VersionService(), NoOpDiskSpaceProvider.Instance,
            QuotinatorMigrations.Baseline);
        await db.InitialiseAsync();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static SourceQuoteDto BuildQuote(string id, string source = "Casablanca", string? character = "Rick Blaine", string? author = null, string quoteText = "Here's looking at you, kid.", string? date = null, Core.Enums.QuoteType type = Core.Enums.QuoteType.Movie) => new()
    {
        Id               = id,
        QuoteText        = quoteText,
        OriginalLanguage = "en",
        Source           = source,
        Character        = character,
        Author           = author,
        Type             = type,
        Date             = date,
    };

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        return conn;
    }

    // ── #219: quote exclusions ───────────────────────────────────────────────────

    /// <summary>
    /// #219 — the fix behind #374's Shawshank Redemption resolution: an excluded quote produces no
    /// action at all, not a decision to make. Its own Source ("The Godfather") would otherwise still
    /// be staged (a real Source, referenced by a real quote elsewhere), proving the exclusion is
    /// scoped to the one quote id, not to the whole file.
    /// </summary>
    [TestMethod]
    public async Task PlanAsync_ExcludedQuote_ProducesNoAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto excluded = BuildQuote("f6111111-1111-4111-8111-111111111111", source: "The Godfather", quoteText: "I'm gonna make him an offer he can't refuse.");
        SourceQuoteDto kept     = BuildQuote("f6211111-1111-4111-8111-111111111111", source: "The Godfather", quoteText: "Keep your friends close, but your enemies closer.");
        QuoteExclusionLookup exclusions = new QuoteExclusionLookup([
            new QuoteExclusionRule { Id = "f6111111-1111-4111-8111-111111111111", Reason = "Test exclusion" },
        ]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [excluded, kept], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, quoteExclusions: exclusions);

        Assert.DoesNotContain(a => a.EntityId == "f6111111-1111-4111-8111-111111111111", actions, "An excluded quote must produce no action of any kind");
        Assert.ContainsSingle(a => a.EntityId == "f6211111-1111-4111-8111-111111111111", actions, "The other, unexcluded quote in the same file must still be staged normally");
    }

    /// <summary>
    /// #374 — found live (T2 Docker, a full-corpus reseed): once two dated variants of the same
    /// (Title, Type) already exist, a `sources[]` enrichment entry with no id and no date of its own
    /// (the natural-key fallback in <c>PlanSourcesAsync</c>) crashed with "Sequence contains more than
    /// one element" — the single-row <c>SelectExistingByTitleAndType</c> query this branch still used
    /// could no longer assume at most one row per title once step 6 put Date in the natural key.
    /// </summary>
    [TestMethod]
    public async Task PlanSourcesAsync_NaturalKeyEntryAgainstTwoDatedVariants_DoesNotCrash()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExplicitSourceAsync(conn, Guid.NewGuid().ToString("D"), title: "The Lion King", type: "Movie", date: "1994");
        await SeedExplicitSourceAsync(conn, Guid.NewGuid().ToString("D"), title: "The Lion King", type: "Movie", date: "2019");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [new SourceEntryDto { Title = "The Lion King", Type = Core.Enums.QuoteType.Movie }]);

        Assert.ContainsSingle(a => a.EntityType == "Source", actions, "Must match one existing variant (nearest, since the entry states no date), not crash or create a third row");
    }

    /// <summary>The control for <see cref="PlanAsync_ExcludedQuote_ProducesNoAction"/> — the same two quotes with no exclusion list must both stage.</summary>
    [TestMethod]
    public async Task PlanAsync_NoExclusions_BothQuotesStage()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto q1 = BuildQuote("f6311111-1111-4111-8111-111111111111", source: "The Godfather", quoteText: "I'm gonna make him an offer he can't refuse.");
        SourceQuoteDto q2 = BuildQuote("f6411111-1111-4111-8111-111111111111", source: "The Godfather", quoteText: "Keep your friends close, but your enemies closer.");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [q1, q2], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.ContainsSingle(a => a.EntityId == "f6311111-1111-4111-8111-111111111111", actions);
        Assert.ContainsSingle(a => a.EntityId == "f6411111-1111-4111-8111-111111111111", actions);
    }

    [TestMethod]
    public async Task PlanAsync_BrandNewQuote_StagesAddActionsForQuoteSourceCharacterPerson()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto quote = BuildQuote("11111111-1111-4111-8111-111111111111", author: "Someone");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.HasCount(4, actions, "Quote + Source + Character + Person, all brand new");
        Assert.IsTrue(actions.All(a => a.ActionType.Parsed == ImportActionKind.Add));
        Assert.IsTrue(actions.All(a => a.Status.Parsed == ImportActionStatus.Decided), "Add is never ambiguous");

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(quote.Id, quoteAction.EntityId);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(EntityIdentity.SourceId("Casablanca", "Movie"), sourceAction.EntityId);

        ImportActionEntity characterAction = actions.Single(a => a.EntityType == "Character");
        // #174/ADR 013: CharacterId's stable-id derivation is (sourceId, name, sourceType).
        Assert.AreEqual(EntityIdentity.CharacterId(sourceAction.EntityId, "Rick Blaine", "Movie"), characterAction.EntityId);

        ImportActionEntity personAction = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(EntityIdentity.PersonId("Someone"), personAction.EntityId);
    }

    [TestMethod]
    public async Task PlanAsync_NoCharacterOrAuthor_StagesOnlyQuoteAndSourceActions()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto quote = BuildQuote("21111111-1111-4111-8111-111111111111", character: null);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.HasCount(2, actions);
        Assert.AreSequenceEqual(["Quote", "Source"], [.. actions.Select(a => a.EntityType)], Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task PlanAsync_ExistingSourceCharacterPerson_ReusesRealIds_NoAddActionsForThem()
    {
        using SqliteConnection conn = await OpenConnectionAsync();

        Guid realSourceId    = Guid.NewGuid();
        Guid realCharacterId = Guid.NewGuid();
        Guid realPersonId    = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO Quotinator_Source (Id, Title, Type, DateCreated) VALUES (@Id, 'Casablanca', 'Movie', @now)",
            new { Id = realSourceId, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
        // #174: Characters.SourceType is NOT NULL as of Migration011 (ADR 013).
        await conn.ExecuteAsync("INSERT INTO Quotinator_Character (Id, Name, SourceType, DateCreated) VALUES (@Id, 'Rick Blaine', 'Movie', @now)",
            new { Id = realCharacterId, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
        // #179: Character<->Source is many-to-many via CharacterSources, not a Characters.SourceId column.
        await conn.ExecuteAsync("INSERT INTO Quotinator_CharacterSource (Id, CharacterId, SourceId, DateCreated) VALUES (@Id, @CharacterId, @SourceId, @now)",
            new { Id = Guid.NewGuid(), CharacterId = realCharacterId, SourceId = realSourceId, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
        await conn.ExecuteAsync("INSERT INTO Quotinator_Person (Id, Name, DateCreated) VALUES (@Id, 'Someone', @now)",
            new { Id = realPersonId, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });

        SourceQuoteDto quote = BuildQuote("31111111-1111-4111-8111-111111111111", author: "Someone");
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        // #373: four actions now, not one. The Quote is the only Add; Source, Character and Person each
        // report that they arrived and already matched. The test's own claim — "only the Quote is new" —
        // is unchanged and is what this asserts; "nothing else is reported at all" was never its point.
        Assert.HasCount(1, actions.Where(a => a.ActionType.Parsed == ImportActionKind.Add).ToList(),
            "Only the Quote is new — Source/Character/Person all already exist");
        Assert.HasCount(3, actions.Where(a => a.ActionType.Parsed == ImportActionKind.Unchanged).ToList(),
            "…and each of those three says so, rather than vanishing from the report.");
        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");

        QuoteActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        // GuidExtensions.ToCanonicalId (and GuidHandler, given RemoveTypeMap) render every Guid column
        // as lowercase "D"-format TEXT (ADR 012) — the resolved id must match that convention.
        Assert.AreEqual(realSourceId.ToString("D"), payload.SourceId, "Must resolve to the real existing Source id, not a stable id");
        Assert.AreEqual(realCharacterId.ToString("D"), payload.CharacterId);
        Assert.AreEqual(realPersonId.ToString("D"), payload.PersonId);
    }

    // ── #174/ADR 013: Character global identity, Series-scoped cross-Source resolution ──────────

    private static async Task<string> SeedGlobalCharacterAsync(SqliteConnection conn, string name, string sourceId, string sourceType)
    {
        Guid characterId = Guid.NewGuid();
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("INSERT INTO Quotinator_Character (Id, Name, SourceType, DateCreated) VALUES (@Id, @Name, @SourceType, @now)",
            new { Id = characterId, Name = name, SourceType = sourceType, now });
        await conn.ExecuteAsync("INSERT INTO Quotinator_CharacterSource (Id, CharacterId, SourceId, DateCreated) VALUES (@Id, @CharacterId, @SourceId, @now)",
            new { Id = Guid.NewGuid(), CharacterId = characterId, SourceId = sourceId, now });
        return characterId.ToString("D");
    }

    private static async Task<string> SeedSourceAsync(SqliteConnection conn, string title, string type = "Movie", string? seriesId = null)
    {
        Guid sourceId = Guid.NewGuid();
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("INSERT INTO Quotinator_Source (Id, Title, Type, SeriesId, DateCreated) VALUES (@Id, @Title, @Type, @SeriesId, @now)",
            new { Id = sourceId, Title = title, Type = type, SeriesId = seriesId, now });
        return sourceId.ToString("D");
    }

    [TestMethod]
    public async Task ResolveCharacterAsync_ExistingGlobalCharacter_ReusesRealId()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string existingSourceId = await SeedSourceAsync(conn, "Existing Film");
        string existingCharacterId = await SeedGlobalCharacterAsync(conn, "Gandalf", existingSourceId, "Movie");

        SourceQuoteDto quote = BuildQuote("e1111111-1111-4111-8111-111111111111", source: "Existing Film", character: "Gandalf");
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        // #373: scoped to Add, which is what this test's own message claims. It previously asserted no
        // Character action of any kind — true only because an existing entity produced nothing at all,
        // and now false because it produces an Unchanged saying it arrived and matched.
        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Character" && a.ActionType.Parsed == ImportActionKind.Add),
            "Already linked to this exact Source — reused, no Add staged");
        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        QuoteActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual(existingCharacterId, payload.CharacterId);
    }

    /// <summary>ADR 013 Decision 7: a same-Name, same-Type Character already linked to a DIFFERENT Source that shares this quote's Source's Series must be reused, not duplicated.</summary>
    [TestMethod]
    public async Task ResolveCharacterAsync_SeriesScopedCrossSourceMatch_ReusesExistingCharacter()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string seriesId = await SeedExistingSeriesAsync(conn, "The Lord of the Rings");
        string film1Id = await SeedSourceAsync(conn, "The Fellowship of the Ring", seriesId: seriesId);
        string existingCharacterId = await SeedGlobalCharacterAsync(conn, "Gandalf", film1Id, "Movie");
        await SeedSourceAsync(conn, "The Two Towers", seriesId: seriesId); // not directly referenced — proves the match isn't keyed off a specific pre-known Source row

        SourceQuoteDto quote = BuildQuote("e2111111-1111-4111-8111-111111111111", source: "The Two Towers", character: "Gandalf");
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        // #373: scoped to Add — reuse means no new Character, not that nothing is reported.
        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Character" && a.ActionType.Parsed == ImportActionKind.Add),
            "A Series-scoped cross-Source match is reused directly, like the same-Source case");
        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        QuoteActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual(existingCharacterId, payload.CharacterId, "Must resolve to the existing global Character, not stage a duplicate");
    }

    [TestMethod]
    public async Task ResolveCharacterAsync_DifferingSourceType_NeverReusesExistingCharacter()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string seriesId = await SeedExistingSeriesAsync(conn, "Middle Earth Adaptations");
        string movieSourceId = await SeedSourceAsync(conn, "The Fellowship of the Ring (Film)", type: "Movie", seriesId: seriesId);
        await SeedGlobalCharacterAsync(conn, "Gandalf", movieSourceId, "Movie");
        await SeedSourceAsync(conn, "The Fellowship of the Ring (Book)", type: "Book", seriesId: seriesId);

        SourceQuoteDto quote = BuildQuote("e3111111-1111-4111-8111-111111111111", source: "The Fellowship of the Ring (Book)", character: "Gandalf", type: Core.Enums.QuoteType.Book);
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.ContainsSingle(a => a.EntityType == "Character", actions, "Source.Type anchor (ADR 011) must never be crossed, even within a shared Series");
    }

    [TestMethod]
    public async Task ResolveCharacterAsync_NoKnownSeriesRelationship_CreatesSeparateCharacter()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string unrelatedSourceId = await SeedSourceAsync(conn, "An Unrelated Movie", seriesId: null);
        await SeedGlobalCharacterAsync(conn, "Sam", unrelatedSourceId, "Movie");
        await SeedSourceAsync(conn, "A Different Unrelated Movie", seriesId: null);

        SourceQuoteDto quote = BuildQuote("e4111111-1111-4111-8111-111111111111", source: "A Different Unrelated Movie", character: "Sam");
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.ContainsSingle(a => a.EntityType == "Character", actions, "Same Name, same Type, but no known Series relationship — conservative default must create a new, separate Character");
    }

    [TestMethod]
    public async Task ResolveCharacterAsync_ExistingGlobalCharacter_CaseInsensitiveNameMatch_ReusesRealId()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string existingSourceId = await SeedSourceAsync(conn, "Existing Film");
        string existingCharacterId = await SeedGlobalCharacterAsync(conn, "Gandalf", existingSourceId, "Movie");

        SourceQuoteDto quote = BuildQuote("e5111111-1111-4111-8111-111111111111", source: "Existing Film", character: "GANDALF");
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        // #373: scoped to Add — a case-insensitive match reuses the row, and now says it did.
        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Character" && a.ActionType.Parsed == ImportActionKind.Add),
            "Name matching is case-insensitive — storage keeps original casing, comparison does not");
        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        QuoteActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual(existingCharacterId, payload.CharacterId);
    }

    [TestMethod]
    public async Task PlanAsync_ExistingQuote_ReviewPolicy_StagesPendingModifyActionWithNoMergedFields()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "41111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [BuildQuote(id, source: "Casablanca")], Guid.NewGuid(), DuplicateResolutionPolicy.Review);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionKind.Modify, quoteAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Pending, quoteAction.Status.Parsed);
        Assert.IsNull(quoteAction.MergedFields, "Pending actions have no resolved values yet");
        Assert.IsNotNull(quoteAction.ExistingValue);
    }

    [TestMethod]
    public async Task PlanAsync_ExistingQuote_NewestWinsPolicy_StagesDecidedModifyActionWithResolvedMergedFields()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "51111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [BuildQuote(id, source: "Casablanca")], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionKind.Modify, quoteAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed);
        Assert.IsNotNull(quoteAction.MergedFields, "Non-Review policies resolve immediately at staging time");
    }

    [TestMethod]
    public async Task PlanAsync_QuoteAlreadyComplete_ChangedFields_StagesBlockedNotModify()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "41211111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id, completenessStatus: "Complete");

        SourceQuoteDto quote = BuildQuote(id, source: "Casablanca", quoteText: "A different line entirely.");
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Blocked, quoteAction.Status.Parsed, "A Complete quote must never silently accept a Modify");
        Assert.IsNull(quoteAction.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanAsync_QuoteAlreadyComplete_SkipPolicy_DoesNotBlock()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "41311111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id, completenessStatus: "Complete");

        SourceQuoteDto quote = BuildQuote(id, source: "Casablanca", quoteText: "A different line entirely.");
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Skip);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed, "Skip's resolved value always equals the existing row — nothing would change, so a Complete row must never block");
    }

    // ── #181: per-source conflict-resolution rule lookup ───────────────────────

    private static readonly System.Text.Json.JsonElement EmptyConflictRuleRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{}");

    // #153: every call site below pairs this rule with existing quoteText "Original text" (from
    // SeedExistingQuoteAsync/SeedExistingQuoteWithCharacterAsync) and incoming "A changed line." — the
    // recorded snapshot must match both real values, or the new staleness check (comparing this
    // snapshot against the current staging run's actual field values) would treat every one of these
    // rules as stale and never reach the auto-resolve behaviour these tests exist to prove.
    private static ConflictResolutionRule BuildQuoteTextKeepRule(string quoteId) => new()
    {
        EntityId = quoteId,
        ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"quoteText":"Original text"}"""),
        IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"quoteText":"A changed line."}"""),
        Fields = [new ConflictResolutionFieldRule { Field = "quoteText", Resolution = FieldResolutionChoice.Keep }],
    };

    [TestMethod]
    public async Task PlanAsync_ReviewPolicy_MatchingRuleCoversTheOnlyChangedField_StagesDecidedNotPending()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "c1111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id);

        SourceQuoteDto quote = BuildQuote(id, source: "Casablanca", quoteText: "A changed line.");
        ConflictRuleLookup rules = new ConflictRuleLookup([BuildQuoteTextKeepRule(id)]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed, "A matching rule for the only changed field must auto-resolve instead of leaving it Pending");
        Assert.IsNotNull(quoteAction.MergedFields, "An auto-resolved action already has its final values computed, the same as any other Decided action");
        QuoteActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.MergedFields!)!;
        Assert.AreEqual("Original text", payload.Fields.QuoteText, "Keep must resolve to the existing side's value");
    }

    [TestMethod]
    public async Task PlanAsync_ReviewPolicy_RuleCoversOnlySomeChangedFields_StillStagesPending()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "c2111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteWithCharacterAsync(conn, id, quoteText: "Original text", characterName: "Rick Blaine");

        SourceQuoteDto quote = BuildQuote(id, source: "Casablanca", quoteText: "A changed line.", character: "Ilsa Lund");
        ConflictRuleLookup rules = new ConflictRuleLookup([BuildQuoteTextKeepRule(id)]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Pending, quoteAction.Status.Parsed, "The character field is also ambiguous and has no matching rule — a partial rule match must not auto-resolve the whole action");
        Assert.IsNull(quoteAction.MergedFields, "Pending actions have no resolved values yet");
    }

    [TestMethod]
    public async Task PlanAsync_ReviewPolicy_NonMatchingRuleLookup_StagesPendingAsToday()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "c3111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id);

        SourceQuoteDto quote = BuildQuote(id, source: "Casablanca", quoteText: "A changed line.");
        ConflictRuleLookup rules = new ConflictRuleLookup([BuildQuoteTextKeepRule("00000000-0000-4000-8000-000000000000")]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Pending, quoteAction.Status.Parsed, "A rule for a different quote id must not affect this one — regression guard matching pre-#181 behaviour");
    }

    [TestMethod]
    public async Task PlanAsync_ReviewPolicy_MatchingRuleButCompletenessGuardBlocks_StillStagesBlockedNotDecided()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "c4111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id, completenessStatus: "Complete");

        SourceQuoteDto quote = BuildQuote(id, source: "Casablanca", quoteText: "A changed line.");
        ConflictRuleLookup rules = new ConflictRuleLookup([BuildQuoteTextKeepRule(id)]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Blocked, quoteAction.Status.Parsed, "A matching rule must never bypass CompletenessGuard — a Complete row still blocks a silent overwrite");
    }

    // ── #153: a Custom-resolution rule also applies to a brand-new Add, not just a later Modify ──

    private static ConflictResolutionRule BuildCharacterCustomRule(string quoteId, string customValue) => new()
    {
        EntityId = quoteId,
        ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"character":null}"""),
        IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"character":null}"""),
        Fields = [new ConflictResolutionFieldRule { Field = "character", Resolution = FieldResolutionChoice.Custom, CustomValue = customValue }],
    };

    [TestMethod]
    public async Task PlanAsync_BrandNewQuote_MatchingCustomRule_CorrectsFieldOnAdd()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "d1111111-1111-4111-8111-111111111111";
        SourceQuoteDto quote = BuildQuote(id, source: "Airplane!", character: null);
        ConflictRuleLookup rules = new ConflictRuleLookup([BuildCharacterCustomRule(id, "Steve McCroskey")]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionKind.Add, quoteAction.ActionType.Parsed, "This is still a genuine first-ever encounter, not a Modify");
        QuoteActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual("Steve McCroskey", payload.Fields.Character, "A matching Custom rule must correct the field on a brand-new Add, not only on a later Modify");
    }

    [TestMethod]
    public async Task PlanAsync_BrandNewQuote_MatchingCustomRule_CharacterResolvesAgainstCorrectedValue()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "d2111111-1111-4111-8111-111111111111";
        SourceQuoteDto quote = BuildQuote(id, source: "Airplane!", character: null);
        ConflictRuleLookup rules = new ConflictRuleLookup([BuildCharacterCustomRule(id, "Steve McCroskey")]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        // Without the fix, Character resolution runs against the raw (null) value and no Character
        // action is staged at all — the corrected text would show on the Quote but never link to a
        // real Character entity.
        ImportActionEntity? characterAction = actions.SingleOrDefault(a => a.EntityType == "Character");
        Assert.IsNotNull(characterAction, "The corrected character value must also drive Character entity resolution, not just the Quote's own display field");
        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(EntityIdentity.CharacterId(sourceAction.EntityId, "Steve McCroskey", "Movie"), characterAction!.EntityId);
    }

    [TestMethod]
    public async Task PlanAsync_BrandNewQuote_StaleCustomRule_DoesNotApply()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "d3111111-1111-4111-8111-111111111111";
        // The rule was authored assuming "character" comes in as null — this quote's raw incoming
        // character is no longer null (the upstream data changed since the rule was written), so the
        // rule's own recorded snapshot for this exact field no longer matches and it must not apply.
        ConflictResolutionRule staleRule = new ConflictResolutionRule
        {
            EntityId = id,
            ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"character":null}"""),
            IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"character":null}"""),
            Fields = [new ConflictResolutionFieldRule { Field = "character", Resolution = FieldResolutionChoice.Custom, CustomValue = "Steve McCroskey" }],
        };
        SourceQuoteDto quote = BuildQuote(id, source: "Airplane!", character: "Some Newly-Added Value");
        ConflictRuleLookup rules = new ConflictRuleLookup([staleRule]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        QuoteActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual("Some Newly-Added Value", payload.Fields.Character, "A stale rule (recorded snapshot no longer matches this field's real value) must never silently apply, on Add or Modify");
    }

    [TestMethod]
    public async Task PlanAsync_BrandNewQuote_KeepOrReplaceRuleField_IsNoOpOnAdd()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "d4111111-1111-4111-8111-111111111111";
        SourceQuoteDto quote = BuildQuote(id, source: "Casablanca", quoteText: "Here's looking at you, kid.");
        // A Keep/Replace rule has no second side to choose between on a brand-new Add — must be a no-op.
        ConflictRuleLookup rules = new ConflictRuleLookup([BuildQuoteTextKeepRule(id)]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionKind.Add, quoteAction.ActionType.Parsed);
        QuoteActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual("Here's looking at you, kid.", payload.Fields.QuoteText, "Keep/Replace on a first-ever Add must be a no-op, not an error");
    }

    // ── #374: ConflictRuleOutcome — AlreadyApplied resolves, Stale/Retirable both hold ──────────

    /// <summary>A `Custom` rule whose stored value already equals its wanted value must resolve to a
    /// terminal state with nothing left to decide, even though the incoming file still carries the
    /// wrong value the rule was written to correct — verification row 16.</summary>
    [TestMethod]
    public async Task AlreadyAppliedRule_LeavesNothingPending()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e1111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id); // stores QuoteText = "Original text"

        SourceQuoteDto quote = BuildQuote(id, source: "Casablanca", quoteText: "A changed line.");
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = id,
            ExistingRecord = EmptyConflictRuleRecord,
            IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"quoteText":"A changed line."}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "quoteText", Resolution = FieldResolutionChoice.Custom, CustomValue = "Original text" }],
        };
        ConflictRuleLookup rules = new ConflictRuleLookup([rule]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreNotEqual(ImportActionStatus.Pending, quoteAction.Status.Parsed,
            "The stored value ('Original text') already equals the rule's wanted value — nothing left to decide");
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed);
    }

    /// <summary>The control for <see cref="AlreadyAppliedRule_LeavesNothingPending"/> — a genuinely
    /// stale rule (incoming side moved since authoring) must still hold the action for review, exactly
    /// as it did before #374. Verification row 17, first half.</summary>
    [TestMethod]
    public async Task StaleRule_StillStagesStale()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e2111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id); // stores QuoteText = "Original text"

        SourceQuoteDto quote = BuildQuote(id, source: "Casablanca", quoteText: "A different changed line.");
        ConflictResolutionRule rule = new ConflictResolutionRule
        {
            EntityId       = id,
            ExistingRecord = EmptyConflictRuleRecord,
            // Recorded incoming ("A changed line.") no longer matches this run's real incoming
            // ("A different changed line.") — the rule's own recorded snapshot has moved.
            IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"quoteText":"A changed line."}"""),
            Fields         = [new ConflictResolutionFieldRule { Field = "quoteText", Resolution = FieldResolutionChoice.Custom, CustomValue = "Original text" }],
        };
        ConflictRuleLookup rules = new ConflictRuleLookup([rule]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Stale, quoteAction.Status.Parsed, "A rule whose incoming side has moved must hold the action for review, not silently apply or resolve");
    }

    /// <summary>The other half of row 17's control pair — a genuine conflict with no matching rule at
    /// all must still stage Pending, unaffected by #374's outcome split.</summary>
    [TestMethod]
    public async Task ConflictWithNoRule_StillStagesPending()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e3111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id); // stores QuoteText = "Original text"

        SourceQuoteDto quote = BuildQuote(id, source: "Casablanca", quoteText: "A changed line.");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Pending, quoteAction.Status.Parsed, "No rule at all must still leave a genuine conflict Pending, unaffected by the outcome split");
    }

    // ── #181: source-title alias lookup ────────────────────────────────────────

    private static async Task SeedExistingQuoteWithSourceAsync(SqliteConnection conn, string quoteId, string sourceId, string sourceTitle, string sourceType, string quoteText)
    {
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("INSERT INTO Quotinator_Source (Id, Title, Type, DateCreated) VALUES (@Id, @sourceTitle, @sourceType, @now)",
            new { Id = sourceId, sourceTitle, sourceType, now });
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Quote (Id, QuoteText, OriginalLanguage, SourceId, DateCreated) VALUES (@Id, @quoteText, 'en', @SourceId, @now)",
            new { Id = quoteId, quoteText, SourceId = sourceId, now });
    }

    [TestMethod]
    public async Task PlanAsync_SourceAliasMatches_ResolvesToExistingCanonicalSource_NoSpuriousSourceAdd()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string canonicalSourceId = Guid.NewGuid().ToString();
        await SeedExplicitSourceAsync(conn, canonicalSourceId, title: "The Avengers", type: "Movie", date: null);

        SourceQuoteDto quote   = BuildQuote("d1111111-1111-4111-8111-111111111111", source: "Marvel's The Avengers", character: null);
        SourceAliasLookup aliases = new SourceAliasLookup([
            new SourceAliasRule { Title = "Marvel's The Avengers", Type = "movie", CanonicalTitle = "The Avengers", CanonicalType = "movie" },
        ]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        // #373: scoped to Add, which is what "no new SourceEntity Add" already said.
        Assert.DoesNotContain(
            a => a.EntityType == "Source" && a.ActionType.Parsed == ImportActionKind.Add, actions,
            "The alias must resolve to the already-existing canonical Source — no new SourceEntity Add should be staged");
        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        QuoteActionPayloadDto payload     = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual(canonicalSourceId, payload.SourceId, "The quote must link to the existing canonical Source, not a spurious alias-derived one");
    }

    /// <summary>
    /// Reproduces the Zootopia-class bug found live during #181's own title-consistency review: a
    /// ConflictResolutionRule correcting a Quote's own displayed `type` field ran too late to prevent
    /// ResolveSourceAsync from already having staged a spurious Source Add under the wrong raw type.
    /// The alias mechanism fixes this by normalising type before ResolveSourceAsync ever runs, so no
    /// ConflictResolutionRule is even needed for this case any more.
    /// </summary>
    [TestMethod]
    public async Task PlanAsync_ModifyPathWithTypeMismatch_AliasAppliedBeforeSourceResolution_NoSpuriousSourceCreated()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string quoteId  = "e1111111-1111-4111-8111-111111111111";
        string sourceId = Guid.NewGuid().ToString();
        await SeedExistingQuoteWithSourceAsync(conn, quoteId, sourceId, "Zootopia", "Movie", "Original text.");

        SourceQuoteDto quote   = BuildQuote(quoteId, source: "Zootopia", quoteText: "Original text.", type: Core.Enums.QuoteType.Anime);
        SourceAliasLookup aliases = new SourceAliasLookup([
            new SourceAliasRule { Title = "Zootopia", Type = "anime", CanonicalTitle = "Zootopia", CanonicalType = "movie" },
        ]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        // #373: scoped to Add — "no spurious Source created" is about creation, not about reporting.
        Assert.DoesNotContain(
            a => a.EntityType == "Source" && a.ActionType.Parsed == ImportActionKind.Add, actions,
            "The alias must normalise type before Source resolution runs — no spurious anime-typed Source should ever be staged");
        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed);
        QuoteActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.MergedFields!)!;
        Assert.AreEqual(sourceId, payload.SourceId, "Must resolve to the original existing Source id, not a new alias-derived one");
    }

    [TestMethod]
    public async Task PlanAsync_NoSourceAliasesProvided_RawTitleUsedAsBefore()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto quote = BuildQuote("d2111111-1111-4111-8111-111111111111", source: "Marvel's The Avengers", character: null);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        SourceActionPayloadDto payload      = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.IncomingValue!)!;
        Assert.AreEqual("Marvel's The Avengers", payload.Title, "With no alias lookup provided, the raw incoming title is used unchanged — regression guard matching pre-#181 behaviour");
    }

    // ── #374: a case-only difference on a quote's own content is genuinely ambiguous ────
    //
    // Found live (T2 Docker, 2026-09-04): a case-only quote/character difference was silently resolved
    // by the old case-insensitive-everywhere comparison. Developer decision, same day: this is
    // genuinely ambiguous — a correction or an unwanted downgrade — and only a human can tell which, so
    // it must be surfaced for review rather than silently resolved.
    //
    // Scoped to quoteText/character, not source: the developer's own wording named "quote, title or
    // character", but source was found live (against the real bundled corpus) to be the wrong field for
    // this — it is never independently persisted per quote (`Sql.Quotes.SelectRawById` builds it from
    // `s.Title AS Source`, a join to the already-resolved Source row), and real upstream data routinely
    // spells the same film's title with different, harmless casing across different quote lines (14
    // such cases measured in NikhilNamal17 alone). Making it case-sensitive turned every one into a
    // permanent false "needs review" conflict with nothing genuine to decide. See
    // `QuoteFieldMerge.CaseSensitiveContentFields`'s own XML doc for the full account.

    /// <summary>
    /// Reproduces the shape of the live defect: a quote's own <c>character</c> field differs from what
    /// is stored only by case, with nothing else genuinely ambiguous. Before the fix this resolved
    /// silently (kept the existing casing) and the action reached <c>Decided</c>; after the fix it must
    /// stage <c>Pending</c> so a human decides whether the incoming casing is a correction or a mistake.
    /// </summary>
    [TestMethod]
    public async Task PlanAsync_QuoteCharacterDiffersOnlyByCase_StagesPendingForReview()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string quoteId = "f1111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteWithCharacterAsync(conn, quoteId, "Frankly, my dear, I don't give a damn.", "Rhett Butler");

        SourceQuoteDto quote = BuildQuote(quoteId, source: "Casablanca", character: "rhett butler",
            quoteText: "Frankly, my dear, I don't give a damn.");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(
            conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionKind.Modify, quoteAction.ActionType.Parsed,
            "A case-only difference is a real difference — not Unchanged");
        Assert.AreEqual(ImportActionStatus.Pending, quoteAction.Status.Parsed,
            "A case-only difference on quoteText/character must be surfaced for a human decision, not silently resolved");
    }

    /// <summary>Control for the row above: an exact match (same casing) must not become ambiguous.</summary>
    [TestMethod]
    public async Task PlanAsync_QuoteCharacterExactMatch_StaysUnchanged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string quoteId = "f2111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteWithCharacterAsync(conn, quoteId, "Frankly, my dear, I don't give a damn.", "Rhett Butler");

        SourceQuoteDto quote = BuildQuote(quoteId, source: "Casablanca", character: "Rhett Butler",
            quoteText: "Frankly, my dear, I don't give a damn.");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(
            conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionKind.Unchanged, quoteAction.ActionType.Parsed);
    }

    /// <summary>
    /// A curator who has already decided a specific casing correction can still resolve it via a
    /// <c>ConflictResolutionRule</c> — the field is only ambiguous by *default*, not permanently
    /// unresolvable. This is the "or add it as a rule" escape hatch #153's own mechanism already
    /// provides for every other field.
    /// </summary>
    [TestMethod]
    public async Task PlanAsync_QuoteCharacterDiffersOnlyByCase_MatchingConflictRuleStillResolves()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string quoteId = "f3111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteWithCharacterAsync(conn, quoteId, "Frankly, my dear, I don't give a damn.", "Rhett Butler");

        SourceQuoteDto quote = BuildQuote(quoteId, source: "Casablanca", character: "rhett butler",
            quoteText: "Frankly, my dear, I don't give a damn.");
        ConflictRuleLookup rules = new ConflictRuleLookup([new ConflictResolutionRule
        {
            EntityId = quoteId,
            ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"character":"Rhett Butler"}"""),
            IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"character":"rhett butler"}"""),
            Fields = [new ConflictResolutionFieldRule { Field = "character", Resolution = FieldResolutionChoice.Replace }],
        }]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(
            conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed,
            "A rule that already covers this exact casing correction resolves it — ambiguous by default, not unresolvable");
    }

    // ── #153: SourceAliasRule staleness ──────────────────────────────────────────

    /// <summary>
    /// Simulates a genuine rename: the Source that was originally created under exactly the alias's
    /// own recorded canonical (title, type) — and therefore carries the id that pair deterministically
    /// hashes to (<see cref="EntityIdentity.SourceId(string, string)"/>, fixed at creation, never recomputed on a later
    /// Modify) — has since had its Title changed away from that canonical value. The alias file was
    /// never updated to match. Deliberately does NOT test "no Source with the canonical title exists
    /// at all" as stale — found live via Docker T2 that an earlier version of this check conflated that
    /// case (a completely legitimate first-time, alias-guided creation) with a genuine rename, producing
    /// false positives against every real bundled alias whose canonical Source hadn't been created by
    /// an earlier file yet. See <see cref="PlanAsync_SourceAliasNoCanonicalSourceYet_FirstTimeCreation_NotStale"/>
    /// for that regression guard.
    /// </summary>
    [TestMethod]
    public async Task PlanAsync_SourceAliasStale_CanonicalSourceRenamedAway_AddPathStagesQuoteAsStale()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string renamedSourceId = EntityIdentity.SourceId("The Avengers", "movie");
        await SeedExplicitSourceAsync(conn, renamedSourceId, title: "The Avengers (Renamed)", type: "Movie", date: null);

        SourceQuoteDto quote   = BuildQuote("f1111111-1111-4111-8111-111111111111", source: "Marvel's The Avengers", character: null);
        SourceAliasLookup aliases = new SourceAliasLookup([
            new SourceAliasRule { Title = "Marvel's The Avengers", Type = "movie", CanonicalTitle = "The Avengers", CanonicalType = "movie" },
        ]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Stale, quoteAction.Status.Parsed, "The row the alias's canonical pair would hash to now has a different live Title — a genuine rename, must never be silently trusted");
        Assert.IsNull(quoteAction.MergedFields, "A Stale action has nothing resolved yet, same as Pending/Blocked");
    }

    [TestMethod]
    public async Task PlanAsync_SourceAliasStale_ModifyPath_StagesQuoteAsStaleNotDecided()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string quoteId  = "f2111111-1111-4111-8111-111111111111";
        string sourceId = Guid.NewGuid().ToString();
        await SeedExistingQuoteWithSourceAsync(conn, quoteId, sourceId, "Zootopia", "Movie", "Original text.");
        string renamedCanonicalId = EntityIdentity.SourceId("Zootopia (Canonical)", "movie");
        await SeedExplicitSourceAsync(conn, renamedCanonicalId, title: "Zootopia (Canonical, Renamed)", type: "Movie", date: null);

        SourceQuoteDto quote   = BuildQuote(quoteId, source: "Zootopia", quoteText: "A changed line.", type: Core.Enums.QuoteType.Anime);
        SourceAliasLookup aliases = new SourceAliasLookup([
            new SourceAliasRule { Title = "Zootopia", Type = "anime", CanonicalTitle = "Zootopia (Canonical)", CanonicalType = "movie" },
        ]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Stale, quoteAction.Status.Parsed, "The alias's canonical Source has been renamed away since authoring — must not silently resolve this Modify");
        Assert.IsNull(quoteAction.MergedFields, "A Stale action has nothing resolved yet, same as Pending/Blocked");
    }

    /// <summary>
    /// Regression guard for the exact bug found live via Docker T2 (#153): an alias whose canonical
    /// Source has never existed at all — the common, legitimate case of an alias guiding the
    /// first-ever creation of a Source under its correct name (e.g. a brand-new database, or the first
    /// bundled file to ever mention this title) — must resolve normally, not be treated as stale.
    /// </summary>
    [TestMethod]
    public async Task PlanAsync_SourceAliasNoCanonicalSourceYet_FirstTimeCreation_NotStale()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto quote   = BuildQuote("f4111111-1111-4111-8111-111111111111", source: "Marvel's The Avengers", character: null);
        SourceAliasLookup aliases = new SourceAliasLookup([
            new SourceAliasRule { Title = "Marvel's The Avengers", Type = "movie", CanonicalTitle = "The Avengers", CanonicalType = "movie" },
        ]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed, "No Source has ever existed under this canonical name yet — this is a legitimate first-time creation, not staleness");
        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        SourceActionPayloadDto payload      = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.IncomingValue!)!;
        Assert.AreEqual("The Avengers", payload.Title, "The new SourceEntity must be created under the alias's canonical title, not the raw incoming one");
    }

    [TestMethod]
    public async Task PlanAsync_SourceAliasFresh_CanonicalSourceExists_RegressionStillDecided()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string canonicalSourceId = Guid.NewGuid().ToString();
        await SeedExplicitSourceAsync(conn, canonicalSourceId, title: "The Avengers", type: "Movie", date: null);

        SourceQuoteDto quote   = BuildQuote("f3111111-1111-4111-8111-111111111111", source: "Marvel's The Avengers", character: null);
        SourceAliasLookup aliases = new SourceAliasLookup([
            new SourceAliasRule { Title = "Marvel's The Avengers", Type = "movie", CanonicalTitle = "The Avengers", CanonicalType = "movie" },
        ]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed, "A fresh alias (canonical Source still exists) must resolve normally, not be treated as stale");
    }

    // ── #374, step 8: an alias corrects a wrong date ────────────────────────────

    /// <summary>
    /// #374, verification row 28 — planner-level proof, not just the lookup in isolation: a quote whose
    /// raw entry claims a wrong date (a typo, e.g. "1958" for a 1985 film) is corrected to the
    /// already-correct dated Source, not left to fragment into its own spurious second variant.
    /// </summary>
    [TestMethod]
    public async Task PlanAsync_DatedAliasCorrectsWrongDate_ResolvesToCanonicalDatedSource()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto correctQuote = BuildQuote("f4111111-1111-4111-8111-111111111111", source: "Back to the Future", date: "1985");
        SourceQuoteDto typoQuote    = BuildQuote("f4211111-1111-4111-8111-111111111111", source: "Back to the future", date: "1958", quoteText: "Great Scott!");
        SourceAliasLookup aliases = new SourceAliasLookup([
            new SourceAliasRule { Title = "Back to the future", Type = "movie", Date = "1958", CanonicalTitle = "Back to the Future", CanonicalType = "movie", CanonicalDate = "1985" },
        ]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [correctQuote, typoQuote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        Assert.ContainsSingle(a => a.EntityType == "Source", actions, "The corrected date must resolve to the same single Source, not fragment into a second variant");
        ImportActionEntity correctAction = actions.Single(a => a.EntityType == "Quote" && a.EntityId == "f4111111-1111-4111-8111-111111111111");
        ImportActionEntity typoAction    = actions.Single(a => a.EntityType == "Quote" && a.EntityId == "f4211111-1111-4111-8111-111111111111");
        QuoteActionPayloadDto correctPayload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(correctAction.IncomingValue!)!;
        QuoteActionPayloadDto typoPayload    = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(typoAction.IncomingValue!)!;
        Assert.AreEqual(correctPayload.SourceId, typoPayload.SourceId);
        Assert.AreEqual(ImportActionStatus.Decided, typoAction.Status.Parsed, "A dated alias resolves the conflict outright — nothing left for a curator to decide");
    }

    /// <summary>
    /// #374, verification row 30 — the control for row 28/step 8: without an alias to correct it, a
    /// wrong date is indistinguishable from a genuinely distinct work (step 6's own design, "residue is
    /// enhancement material, not a blocker" — see the plan's step 8 text). It must still resolve cleanly
    /// to its own new Source variant, with nothing left pending, exactly like <see cref="SameTitleDifferentDate_ResolvesToTwoSources"/>.
    /// </summary>
    [TestMethod]
    public async Task AWrongDatedSource_StillLeavesNothingPending()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto correctQuote = BuildQuote("f4311111-1111-4111-8111-111111111111", source: "Back to the Future", date: "1985");
        SourceQuoteDto typoQuote    = BuildQuote("f4411111-1111-4111-8111-111111111111", source: "Back to the future", date: "1958", quoteText: "Great Scott!");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [correctQuote, typoQuote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.HasCount(2, [.. actions.Where(a => a.EntityType == "Source")], "An uncorrected wrong date is a real, distinguishable variant — not a reported conflict");
        Assert.DoesNotContain(a => a.EntityType == "Quote" && a.Status.Parsed != ImportActionStatus.Decided, actions,
            "Nothing is left pending — the uncorrected wrong date leaves a spurious but resolved second Source, not an unresolved one");
    }

    private static async Task SeedExistingQuoteWithCharacterAsync(SqliteConnection conn, string id, string quoteText, string characterName)
    {
        string now          = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        Guid sourceId     = Guid.NewGuid();
        Guid characterId  = Guid.NewGuid();
        Guid characterSourceId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO Quotinator_Source (Id, Title, Type, DateCreated) VALUES (@Id, 'Casablanca', 'Movie', @now)", new { Id = sourceId, now });
        await conn.ExecuteAsync("INSERT INTO Quotinator_Character (Id, Name, DateCreated) VALUES (@Id, @characterName, @now)", new { Id = characterId, characterName, now });
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_CharacterSource (Id, CharacterId, SourceId, DateCreated) VALUES (@Id, @CharacterId, @SourceId, @now)",
            new { Id = characterSourceId, CharacterId = characterId, SourceId = sourceId, now });
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Quote (Id, QuoteText, OriginalLanguage, SourceId, CharacterId, DateCreated) VALUES (@Id, @quoteText, 'en', @SourceId, @CharacterId, @now)",
            new { Id = id, quoteText, SourceId = sourceId, CharacterId = characterId, now });
    }

    [TestMethod]
    public async Task PlanAsync_TwoQuotesInSameBatchReferencingSameNewSource_StagesOnlyOneSourceAddAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto q1 = BuildQuote("61111111-1111-4111-8111-111111111111", character: "Rick Blaine");
        SourceQuoteDto q2 = BuildQuote("71111111-1111-4111-8111-111111111111", character: "Ilsa Lund");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [q1, q2], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.ContainsSingle(a => a.EntityType == "Source", actions, "Both quotes share the same Source — must be staged once, not twice");
    }

    [TestMethod]
    public async Task ResolveSourceAsync_QuoteWithDate_StagesSourceAddCarryingThatDate()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto quote = BuildQuote("61211111-1111-4111-8111-111111111111", date: "1993");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        SourceActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.IncomingValue!)!;
        Assert.AreEqual("1993", payload.Date, "The resolving quote's own Date must carry through to the staged Source Add payload");
    }

    /// <summary>
    /// #374: renamed and reversed from `ResolveSourceAsync_TwoQuotesSameSourceDifferentDates_FirstQuotesDateWins`
    /// — verification row 18. Two works sharing a title and differing in date (the Lion King shape,
    /// animated 1994 / live-action 2019) must become two distinct Source rows, not collapse onto
    /// whichever quote's date happened to be seen first. Date is now part of a Source's natural key
    /// (step 6), so the two rows coexist under the same widened UNIQUE constraint instead of colliding.
    /// </summary>
    [TestMethod]
    public async Task SameTitleDifferentDate_ResolvesToTwoSources()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto q1 = BuildQuote("61311111-1111-4111-8111-111111111111", character: "Rick Blaine", date: "1994");
        SourceQuoteDto q2 = BuildQuote("61411111-1111-4111-8111-111111111111", character: "Ilsa Lund", date: "2019");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [q1, q2], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        List<ImportActionEntity> sourceActions = [.. actions.Where(a => a.EntityType == "Source")];
        Assert.HasCount(2, sourceActions, "Two distinct dates for the same title must stage two distinct Source Add actions, not one");
        Assert.AreNotEqual(sourceActions[0].EntityId, sourceActions[1].EntityId, "The two variants must get distinct ids");
        List<string?> dates = [.. sourceActions.Select(a => System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(a.IncomingValue!)!.Date)];
        Assert.Contains("1994", dates);
        Assert.Contains("2019", dates);
    }

    /// <summary>The control for <see cref="SameTitleDifferentDate_ResolvesToTwoSources"/> — a second
    /// quote for the SAME title and the SAME date within one batch must still be recognised as the same
    /// Source, not treated as yet another new variant.</summary>
    [TestMethod]
    public async Task SameTitleSameDate_ResolvesToOneSource()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto q1 = BuildQuote("61511111-1111-4111-8111-111111111111", character: "Rick Blaine", date: "1994");
        SourceQuoteDto q2 = BuildQuote("61611111-1111-4111-8111-111111111111", character: "Ilsa Lund", date: "1994");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [q1, q2], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.ContainsSingle(a => a.EntityType == "Source", actions, "Both quotes agree on the same date — must resolve to one Source, not two");
    }

    // ── #374: a series-capable type (tv) with no Series data cannot tell "new season" from "wrong ──
    // ── year" — a second date must be reported as a conflict, never silently split into a new Source ──

    /// <summary>
    /// Found live against the real bundled corpus: "Arrow" and "Mr. Robot" each split into two Source
    /// rows once Date joined the natural key, because their remaining un-curated quotes carry a
    /// per-quote year that is simply wrong (#375), not a real season marker — and nothing distinguishes
    /// that from a genuinely new season without Series data to check it against. The second quote must
    /// attach to the same, nearest Source (no fragmentation) and be held for a curator's own decision.
    /// </summary>
    [TestMethod]
    public async Task TvQuoteWithASecondYear_NoSeriesData_AttachesToNearestSourceAndStagesPending()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto q1 = BuildQuote("71711111-1111-4111-8111-111111111111", source: "Arrow", character: "Oliver Queen", date: "2015", type: Core.Enums.QuoteType.Tv);
        SourceQuoteDto q2 = BuildQuote("71811111-1111-4111-8111-111111111111", source: "Arrow", character: "Felicity Smoak", date: "2017", type: Core.Enums.QuoteType.Tv);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [q1, q2], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.ContainsSingle(a => a.EntityType == "Source", actions, "A wrong per-quote year must never fragment a tv show into a second Source row");

        ImportActionEntity secondQuoteAction = actions.Single(a => a.EntityType == "Quote" && a.EntityId == "71811111-1111-4111-8111-111111111111");
        Assert.AreEqual(ImportActionStatus.Pending, secondQuoteAction.Status.Parsed, "The disagreeing year must be reported as a conflict, not silently resolved either way");

        ImportActionEntity firstQuoteAction = actions.Single(a => a.EntityType == "Quote" && a.EntityId == "71711111-1111-4111-8111-111111111111");
        QuoteActionPayloadDto firstPayload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(firstQuoteAction.IncomingValue!)!;
        QuoteActionPayloadDto secondPayload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(secondQuoteAction.IncomingValue!)!;
        Assert.AreEqual(firstPayload.SourceId, secondPayload.SourceId, "Both quotes must resolve to the same, single Arrow Source");
    }

    /// <summary>The control for <see cref="TvQuoteWithASecondYear_NoSeriesData_AttachesToNearestSourceAndStagesPending"/>
    /// — a movie (not series-capable) with the same shape still gets two distinct Source rows, exactly as
    /// <see cref="SameTitleDifferentDate_ResolvesToTwoSources"/> already established. Restated here as an
    /// explicit type-based control so the tv-specific carve-out is proven, not assumed.</summary>
    [TestMethod]
    public async Task MovieQuoteWithASecondYear_StillResolvesToTwoSources()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto q1 = BuildQuote("71911111-1111-4111-8111-111111111111", source: "The Lion King", character: "Simba", date: "1994", type: Core.Enums.QuoteType.Movie);
        SourceQuoteDto q2 = BuildQuote("72011111-1111-4111-8111-111111111111", source: "The Lion King", character: "Nala", date: "2019", type: Core.Enums.QuoteType.Movie);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [q1, q2], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.HasCount(2, [.. actions.Where(a => a.EntityType == "Source")], "A movie's second date is a real, distinguishable variant — not a reported conflict");
        Assert.DoesNotContain(a => a.EntityType == "Quote" && a.Status.Parsed == ImportActionStatus.Pending, actions);
    }

    // ── #245: ResolveSourceAsync backfilling a null Date on an already-existing Source ──────────

    [TestMethod]
    public async Task ResolveSourceAsync_ExistingNullDatedSource_QuoteWithDate_StagesDecidedModifyBackfillingDate()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = Guid.NewGuid().ToString("D");
        await SeedExplicitSourceAsync(conn, sourceId, title: "Casablanca", type: "Movie", date: null, completenessStatus: "Incomplete");
        SourceQuoteDto quote = BuildQuote("c1111111-1111-4111-8111-111111111111", date: "1942");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, sourceAction.ActionType.Parsed, "The Source already exists — this must be a Modify, not a fresh Add");
        Assert.AreEqual(ImportActionStatus.Decided, sourceAction.Status.Parsed, "A background Date backfill needs no human review, matching #191's own Add-payload precedent");
        SourceActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.MergedFields!)!;
        Assert.AreEqual("1942", payload.Date, "The resolving quote's own Date must backfill the existing row's null Date");
    }

    /// <summary>
    /// #374: renamed and reversed from `ResolveSourceAsync_ExistingDatedSource_QuoteWithDifferentDate_NoActionStaged`.
    /// A quote whose date genuinely disagrees with an already-dated Source no longer silently attaches
    /// to that row (the pre-#374 "first-found-wins" bug this whole issue is about) — it stages a new,
    /// distinctly-dated Source variant instead, leaving the original row untouched.
    /// </summary>
    [TestMethod]
    public async Task ResolveSourceAsync_ExistingDatedSource_QuoteWithDifferentDate_StagesNewSourceVariant()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = Guid.NewGuid().ToString("D");
        await SeedExplicitSourceAsync(conn, sourceId, title: "Casablanca", type: "Movie", date: "1942", completenessStatus: "Incomplete");
        SourceQuoteDto quote = BuildQuote("c2111111-1111-4111-8111-111111111111", date: "1999");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Add, sourceAction.ActionType.Parsed, "A genuinely different date must stage a new variant, not silently attach to the existing 1942 row");
        Assert.AreNotEqual(sourceId, sourceAction.EntityId, "The new variant must not reuse the existing, differently-dated row's id");
        SourceActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.IncomingValue!)!;
        Assert.AreEqual("1999", payload.Date);
    }

    /// <summary>
    /// #374 verification row 21 — the control proving steps 6/7 need no id rewrite. A quote whose date
    /// agrees with an already-existing, explicitly-dated Source must still be matched by natural key and
    /// reuse that row's real id — never recomputed via <see cref="EntityIdentity.SourceId(string, string, string?)"/>,
    /// which per ADR 002 governs new rows only. A failure here is the wipe-and-reseed the design exists
    /// to avoid.
    /// </summary>
    [TestMethod]
    public async Task ExistingSource_IsMatchedByNaturalKey_AndKeepsItsId()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = Guid.NewGuid().ToString("D");
        await SeedExplicitSourceAsync(conn, sourceId, title: "Casablanca", type: "Movie", date: "1942", completenessStatus: "Incomplete");
        SourceQuoteDto quote = BuildQuote("c4111111-1111-4111-8111-111111111111", date: "1942");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Unchanged, sourceAction.ActionType.Parsed, "Agreeing dates must resolve to the existing row, untouched");
        Assert.AreEqual(sourceId, sourceAction.EntityId, "The existing row's real id must be reused, never recomputed");
    }

    /// <summary>
    /// A quote naming its source in different casing, with an agreeing date, resolves to the existing
    /// row rather than creating a second one — the quote-path counterpart of
    /// <see cref="PlanSourcesAsync_NoExplicitId_DifferingCasing_MatchesExistingNaturalKey"/>, which
    /// only covers an explicit <c>sources[]</c> declaration.
    /// </summary>
    /// <remarks>
    /// The gap this closes was found in real data by
    /// <c>import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions.md</c>'s duplicate
    /// check, which lists <c>Back to the future</c> beside <c>Back to the Future</c> and
    /// <c>The Silence of the lambs</c> beside <c>The Silence of the Lambs</c> — both reached through a
    /// quote's own <c>source</c> field, not through a declaration. Nothing at unit level asserted that
    /// path was case-insensitive.
    /// </remarks>
    [TestMethod]
    public async Task ResolveSourceAsync_QuoteWithDifferentlyCasedTitle_SameDate_ReusesTheExistingSource()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = Guid.NewGuid().ToString("D");
        await SeedExplicitSourceAsync(conn, sourceId, title: "Back to the Future", type: "Movie", date: "1985");
        SourceQuoteDto quote = BuildQuote("c5111111-1111-4111-8111-111111111111", source: "back to the FUTURE", date: "1985");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == ImportActionEntityTypes.Source);
        Assert.AreEqual(sourceId, sourceAction.EntityId, "A case-only title difference must resolve to the existing row, never a second Source");
        Assert.IsEmpty(actions.Where(a => a.EntityType == ImportActionEntityTypes.Source && a.ActionType.Parsed == ImportActionKind.Add),
            "No Add may be staged — that is the duplicate this assertion exists to prevent");
    }

    /// <summary>
    /// The negative half of the pair above, and the one that makes it mean something: a case-only
    /// difference is reused, but a genuinely different <em>title</em> is not. Without this, "reuses the
    /// existing row" is satisfiable by a build that collapses every Source onto the first one it finds.
    /// </summary>
    [TestMethod]
    public async Task ResolveSourceAsync_QuoteWithGenuinelyDifferentTitle_StagesItsOwnSource()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = Guid.NewGuid().ToString("D");
        await SeedExplicitSourceAsync(conn, sourceId, title: "Back to the Future", type: "Movie", date: "1985");
        SourceQuoteDto quote = BuildQuote("c6111111-1111-4111-8111-111111111111", source: "Back to the Future Part II", date: "1989");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == ImportActionEntityTypes.Source);
        Assert.AreEqual(ImportActionKind.Add, sourceAction.ActionType.Parsed, "A different film is a different Source");
        Assert.AreNotEqual(sourceId, sourceAction.EntityId);
    }

    /// <summary>
    /// A date variant keeps the incoming spelling rather than being absorbed into the first-seen row's
    /// casing, so a casing divergence stays visible instead of being silently normalised away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Normalising here was implemented and then reverted the same day. It cleared the smoke suite's
    /// casing check, but by hiding the disagreement rather than resolving it: whichever spelling
    /// happened to be stored first would become canonical, so a first-seen <c>the mOvie Title</c> would
    /// silently swallow every later, correct <c>The Movie Title</c> with nothing left to notice.
    /// Developer rule, 2026-09-08: <em>casing duplicates need to be explicitly permitted; they should
    /// not be hidden.</em>
    /// </para>
    /// <para>
    /// Explicit permission is a <c>SourceAliasRule</c> naming the canonical spelling — a reviewed line
    /// in a file, not a guess made mid-import. Until one exists, both spellings reach the database and
    /// <c>14-fresh-seed-produces-zero-pending-actions.md</c>'s check B reports them, which is the
    /// intended outcome rather than a defect.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ResolveSourceAsync_DateVariantOfDifferentlyCasedTitle_KeepsBothSpellingsVisible()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = Guid.NewGuid().ToString("D");
        await SeedExplicitSourceAsync(conn, sourceId, title: "Back to the Future", type: "Movie", date: "1985");
        SourceQuoteDto quote = BuildQuote("c7111111-1111-4111-8111-111111111111", source: "Back to the future", date: "1958");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity variant = actions.Single(a => a.EntityType == ImportActionEntityTypes.Source);
        SourceActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(variant.IncomingValue!)!;
        Assert.AreEqual("Back to the future", payload.Title,
            "The incoming spelling must survive — absorbing it into the stored casing would make a wrong first-seen title permanent and invisible");
        Assert.AreNotEqual(sourceId, variant.EntityId, "A differing date is still a distinct variant");
    }

    /// <summary>
    /// An explicitly declared <c>SourceAliasRule</c> is what resolves a casing divergence — the
    /// permitted path the test above deliberately leaves open.
    /// </summary>
    [TestMethod]
    public async Task ResolveSourceAsync_DifferentlyCasedTitle_WithAliasDeclared_ResolvesToTheCanonicalSpelling()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = Guid.NewGuid().ToString("D");
        await SeedExplicitSourceAsync(conn, sourceId, title: "Back to the Future", type: "Movie", date: "1985");
        SourceQuoteDto quote = BuildQuote("c8111111-1111-4111-8111-111111111111", source: "Back to the future", date: "1958");
        SourceAliasLookup aliases = new(
        [
            new SourceAliasRule
            {
                Title          = "Back to the future",
                Type           = "movie",
                CanonicalTitle = "Back to the Future",
                CanonicalType  = "movie",
            },
        ]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(
            conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        Assert.IsEmpty(actions.Where(a =>
            {
                if (a.EntityType != ImportActionEntityTypes.Source || a.IncomingValue is null) return false;
                SourceActionPayloadDto p = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(a.IncomingValue)!;
                return p.Title == "Back to the future";
            }),
            "With the alias declared, the raw spelling must never reach the database");
    }

    [TestMethod]
    public async Task ResolveSourceAsync_ExistingCompleteNullDatedSource_QuoteWithDate_StagesBlockedNotBackfill()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = Guid.NewGuid().ToString("D");
        await SeedExplicitSourceAsync(conn, sourceId, title: "Casablanca", type: "Movie", date: null, completenessStatus: "Complete");
        SourceQuoteDto quote = BuildQuote("c3111111-1111-4111-8111-111111111111", date: "1942");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Blocked, sourceAction.Status.Parsed, "A Complete Source must never have its null Date silently backfilled");
    }

    [TestMethod]
    public async Task PlanAsync_NeverWritesToAnyDomainTable()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        SourceQuoteDto quote = BuildQuote("81111111-1111-4111-8111-111111111111", author: "Someone");

        await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Quote"));
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Source"));
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Character"));
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Person"));
    }

    [TestMethod]
    public async Task PlanAsync_CalledTwiceForSameNewSource_ProducesTheSameStableIdBothTimes()
    {
        using SqliteConnection conn1 = await OpenConnectionAsync();
        IReadOnlyList<ImportActionEntity> actions1 = await ImportActionPlanner.PlanAsync(conn1, [BuildQuote("91111111-1111-4111-8111-111111111111")], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        using SqliteConnection conn2 = await OpenConnectionAsync();
        IReadOnlyList<ImportActionEntity> actions2 = await ImportActionPlanner.PlanAsync(conn2, [BuildQuote("a1111111-1111-4111-8111-111111111111")], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        string sourceId1 = actions1.Single(a => a.EntityType == "Source").EntityId;
        string sourceId2 = actions2.Single(a => a.EntityType == "Source").EntityId;
        Assert.AreEqual(sourceId1, sourceId2, "Same title+type must always produce the same stable id, across independent PlanAsync calls");
    }

    /// <summary>
    /// #373: seeds the Source, Character and Quote exactly as <see cref="BuildQuote"/> describes them,
    /// so re-planning that same quote finds nothing to change.
    /// <para>
    /// Distinct from <see cref="SeedExistingQuoteAsync"/>, which stores <c>'Original text'</c> — a
    /// deliberate mismatch that produces a Modify. Identical content is the case that had no fixture
    /// at all, which is why nothing caught it reporting as modified.
    /// </para>
    /// </summary>
    private static async Task SeedIdenticalQuoteAsync(SqliteConnection conn, string id)
    {
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        Guid sourceId    = Guid.NewGuid();
        Guid characterId = Guid.NewGuid();

        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Source (Id, Title, Type, DateCreated) VALUES (@Id, 'Casablanca', 'Movie', @now)",
            new { Id = sourceId, now });
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Character (Id, Name, SourceType, DateCreated) VALUES (@Id, 'Rick Blaine', 'Movie', @now)",
            new { Id = characterId, now });
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_CharacterSource (Id, CharacterId, SourceId, DateCreated) VALUES (@Id, @CharacterId, @SourceId, @now)",
            new { Id = Guid.NewGuid(), CharacterId = characterId, SourceId = sourceId, now });
        await conn.ExecuteAsync(
            """
            INSERT INTO Quotinator_Quote (Id, QuoteText, OriginalLanguage, SourceId, CharacterId, DateCreated)
            VALUES (@Id, 'Here''s looking at you, kid.', 'en', @SourceId, @CharacterId, @now);
            """,
            new { Id = id, SourceId = sourceId, CharacterId = characterId, now });
    }

    // ── #373: every incoming item is accounted for, and identical content is not a modification ────

    /// <summary>
    /// The issue's core: re-importing a quote that exactly matches what is stored is not a
    /// modification. `effectiveChanged` is already empty in this case; the planner ignores that.
    /// </summary>
    [TestMethod]
    public async Task ReimportingIdenticalContent_ReportsUnchangedNotModified()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "a1111111-1111-4111-8111-111111111111";
        await SeedIdenticalQuoteAsync(conn, id);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(
            conn, [BuildQuote(id)], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionKind.Unchanged, quoteAction.ActionType.Parsed,
            "Nothing would be written for this row, so calling it a modification is a false report.");
    }

    /// <summary>
    /// The control the test above needs: a planner reporting nothing at all would satisfy it. This
    /// asserts real actions, naming real entity types.
    /// </summary>
    [TestMethod]
    public async Task ReimportingIdenticalContent_StillAccountsForEveryIncomingItem()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "a2111111-1111-4111-8111-111111111111";
        await SeedIdenticalQuoteAsync(conn, id);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(
            conn, [BuildQuote(id)], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.IsNotEmpty(actions, "An import that accounts for nothing is not an import that changed nothing.");
        Assert.Contains("Quote", [.. actions.Select(a => a.EntityType)]);
    }

    /// <summary>
    /// An unchanged row is terminal — nobody is waiting on a decision about content that matches.
    /// </summary>
    [TestMethod]
    public async Task ReimportingIdenticalContent_LeavesNothingPending()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "a3111111-1111-4111-8111-111111111111";
        await SeedIdenticalQuoteAsync(conn, id);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(
            conn, [BuildQuote(id)], Guid.NewGuid(), DuplicateResolutionPolicy.Review);

        Assert.IsEmpty(actions.Where(a => a.Status.Parsed == ImportActionStatus.Pending),
            "Review policy decides genuine disagreements. Identical content is not one, even under Review.");
    }

    /// <summary>
    /// The sharpest control: a planner classifying everything as unchanged would pass every test above.
    /// </summary>
    [TestMethod]
    public async Task ChangedContent_StillReportsModified()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "a4111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(
            conn, [BuildQuote(id, source: "Casablanca")], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionKind.Modify, quoteAction.ActionType.Parsed,
            "The stored text differs from the incoming text — this one really is a modification.");
    }

    /// <summary>
    /// The other of the two nothings. Content absent from the database is new, and must never be
    /// confused with content that was already correct.
    /// </summary>
    [TestMethod]
    public async Task AbsentContent_ReportsNewNotUnchanged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(
            conn, [BuildQuote("a5111111-1111-4111-8111-111111111111")], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionKind.Add, quoteAction.ActionType.Parsed);
        Assert.IsEmpty(actions.Where(a => a.ActionType.Parsed == ImportActionKind.Unchanged),
            "Nothing here existed beforehand, so nothing can be unchanged.");
    }

    /// <summary>
    /// #373's second half: an already-existing Source, Character or Person produces no action at all
    /// today, so it vanishes from the report entirely — a reader cannot tell it arrived and was
    /// already correct from a file that never mentioned it.
    /// </summary>
    [TestMethod]
    public async Task ExistingReferencedEntities_AreReportedUnchanged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        Guid sourceId    = Guid.NewGuid();
        Guid characterId = Guid.NewGuid();

        await conn.ExecuteAsync("INSERT INTO Quotinator_Source (Id, Title, Type, DateCreated) VALUES (@Id, 'Casablanca', 'Movie', @now)",
            new { Id = sourceId, now });
        await conn.ExecuteAsync("INSERT INTO Quotinator_Character (Id, Name, SourceType, DateCreated) VALUES (@Id, 'Rick Blaine', 'Movie', @now)",
            new { Id = characterId, now });
        await conn.ExecuteAsync("INSERT INTO Quotinator_CharacterSource (Id, CharacterId, SourceId, DateCreated) VALUES (@Id, @CharacterId, @SourceId, @now)",
            new { Id = Guid.NewGuid(), CharacterId = characterId, SourceId = sourceId, now });
        await conn.ExecuteAsync("INSERT INTO Quotinator_Person (Id, Name, DateCreated) VALUES (@Id, 'Someone', @now)",
            new { Id = Guid.NewGuid(), now });

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(
            conn, [BuildQuote("a6111111-1111-4111-8111-111111111111", author: "Someone")], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        List<string> unchangedTypes = [.. actions
            .Where(a => a.ActionType.Parsed == ImportActionKind.Unchanged)
            .Select(a => a.EntityType)];

        foreach (string entityType in (string[])["Source", "Character", "Person"])
        {
            Assert.Contains(entityType, unchangedTypes,
                $"{entityType} arrived and was already correct — saying nothing about it reads as work that never happened.");
        }
    }

    /// <summary>
    /// The control for the test above: reporting existing entities must not stop the planner creating
    /// absent ones, which is its original job.
    /// </summary>
    [TestMethod]
    public async Task AbsentReferencedEntities_AreStillAdded()
    {
        using SqliteConnection conn = await OpenConnectionAsync();

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(
            conn, [BuildQuote("a7111111-1111-4111-8111-111111111111", author: "Someone")], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        List<string> addedTypes = [.. actions
            .Where(a => a.ActionType.Parsed == ImportActionKind.Add)
            .Select(a => a.EntityType)];

        foreach (string entityType in (string[])["Source", "Character", "Person"])
        {
            Assert.Contains(entityType, addedTypes,
                $"{entityType} does not exist yet and must still be created.");
        }
    }

    /// <summary>
    /// #373: the composite entities are planned by their own branch, not as references from a quote,
    /// so they are covered separately rather than assumed to follow from
    /// <see cref="ExistingReferencedEntities_AreReportedUnchanged"/>. Today an id match with nothing
    /// differing is silent reuse — the row arrived, matched, and left no trace.
    /// </summary>
    [TestMethod]
    public async Task ExistingCompositeEntities_AreReportedUnchanged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "a8111111-1111-4111-8111-111111111111";
        await SeedExplicitStageDirectionAsync(conn, id, text: "A shot rings out.", imageUrl: "https://example.com/still.jpg");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(
            conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            stageDirections: [BuildStageDirectionEntry(id, text: "A shot rings out.", imageUrl: "https://example.com/still.jpg")]);

        // Counted before it is read: with no action at all, Single() would throw and the failure would
        // look identical to a test asserting the opposite (#372's own step-1 lesson).
        List<ImportActionEntity> stageDirections = [.. actions.Where(a => a.EntityType == "StageDirection")];
        Assert.HasCount(1, stageDirections,
            "It arrived and matched. Silent reuse leaves a reader unable to tell that from a file that never mentioned it.");
        Assert.AreEqual(ImportActionKind.Unchanged, stageDirections[0].ActionType.Parsed);
    }

    private static async Task SeedExistingQuoteAsync(SqliteConnection conn, string id, string completenessStatus = "Incomplete")
    {
        string now      = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        Guid sourceId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO Quotinator_Source (Id, Title, Type, DateCreated) VALUES (@Id, 'Casablanca', 'Movie', @now)", new { Id = sourceId, now });
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Quote (Id, QuoteText, OriginalLanguage, SourceId, CompletenessStatus, DateCreated) VALUES (@Id, 'Original text', 'en', @SourceId, @CompletenessStatus, @now)",
            new { Id = id, SourceId = sourceId, CompletenessStatus = completenessStatus, now });
    }

    // ── #162: PlanSourcesAsync ────────────────────────────────────────────────

    private static SourceEntryDto BuildSourceEntry(string? id, string title = "Casablanca", Core.Enums.QuoteType type = Core.Enums.QuoteType.Movie, string? date = "1942", string? seriesName = null) => new()
    {
        Id         = id,
        Title      = title,
        Type       = type,
        Date       = date,
        SeriesName = seriesName,
    };

    /// <summary>#180: an enrichment-shaped entry — no explicit id (matched by natural key), no date (not intended to be set), just the Series link.</summary>
    private static SourceEntryDto BuildEnrichmentEntry(string title = "Casablanca", Core.Enums.QuoteType type = Core.Enums.QuoteType.Movie, string? seriesName = "The Hobbit") => new()
    {
        Title      = title,
        Type       = type,
        SeriesName = seriesName,
    };

    private static async Task SeedExplicitSourceAsync(SqliteConnection conn, string id, string title = "Casablanca", string type = "Movie", string? date = "1942", string completenessStatus = "Incomplete", string? seriesId = null)
    {
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Source (Id, Title, Type, Date, SeriesId, CompletenessStatus, DateCreated) VALUES (@Id, @Title, @Type, @Date, @SeriesId, @CompletenessStatus, @now)",
            new { Id = id, Title = title, Type = type, Date = date, SeriesId = seriesId, CompletenessStatus = completenessStatus, now });
    }

    // ── #180: PlanUniverseAsync / PlanSeriesAsync / Source.SeriesId ─────────────

    private static UniverseEntryDto BuildUniverseEntry(string name = "Middle Earth") => new() { Name = name };

    private static SeriesEntryDto BuildSeriesEntry(string name = "The Lord of the Rings", string? universeName = null) => new()
    {
        Name         = name,
        UniverseName = universeName,
    };

    private static async Task<string> SeedExistingSeriesAsync(SqliteConnection conn, string name = "The Lord of the Rings", string? universeId = null)
    {
        string id  = Guid.NewGuid().ToString("D");
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Series (Id, Name, UniverseId, CompletenessStatus, DateCreated) VALUES (@Id, @Name, @UniverseId, 'Incomplete', @now)",
            new { Id = id, Name = name, UniverseId = universeId, now });
        return id;
    }

    private static async Task<string> SeedExistingUniverseAsync(SqliteConnection conn, string name = "Middle Earth")
    {
        string id  = Guid.NewGuid().ToString("D");
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Universe (Id, Name, CompletenessStatus, DateCreated) VALUES (@Id, @Name, 'Incomplete', @now)",
            new { Id = id, Name = name, now });
        return id;
    }

    private static SeasonEntryDto BuildSeasonEntry(int number = 1, string? seriesName = "Avatar: The Last Airbender", string? title = "Book One", string? subtitle = "Water", string? id = null) => new()
    {
        Id         = id,
        Number     = number,
        SeriesName = seriesName,
        Title      = title,
        Subtitle   = subtitle,
    };

    private static async Task<string> SeedExistingSeasonAsync(SqliteConnection conn, string seriesId, int number = 1, string? title = "Book One", string? subtitle = "Water")
    {
        string id  = Guid.NewGuid().ToString("D");
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Season (Id, Number, Title, Subtitle, SeriesId, CompletenessStatus, DateCreated) VALUES (@Id, @Number, @Title, @Subtitle, @SeriesId, 'Incomplete', @now)",
            new { Id = id, Number = number, Title = title, Subtitle = subtitle, SeriesId = seriesId, now });
        return id;
    }

    // ── #373 step 10: the four natural-key match paths ──────────────────────
    // Each of these four sites resolved an id and returned, emitting nothing. The pre-existing
    // "..._ExistingByName_NoActionStaged" tests do not cover this: they assert
    // count(EntityType == X && ActionType != Unchanged) == 0, which emitting nothing already
    // satisfies. These assert the positive — that an Unchanged action is actually staged.

    [TestMethod]
    public async Task PlanUniverseAsync_ExistingByName_StagesUnchangedAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExistingUniverseAsync(conn, "Middle Earth");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            universe: [BuildUniverseEntry("Middle Earth")]);

        Assert.ContainsSingle(actions.Where(a => a.EntityType == ImportActionEntityTypes.Universe && a.ActionType.Parsed == ImportActionKind.Unchanged),
            "A Universe matched by its natural key must report itself as Unchanged, not vanish from the report");
    }

    [TestMethod]
    public async Task PlanSeriesAsync_ExistingByName_StagesUnchangedAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExistingSeriesAsync(conn, "The Lord of the Rings");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            series: [BuildSeriesEntry("The Lord of the Rings")]);

        Assert.ContainsSingle(actions.Where(a => a.EntityType == ImportActionEntityTypes.Series && a.ActionType.Parsed == ImportActionKind.Unchanged),
            "A Series matched by its natural key must report itself as Unchanged, not vanish from the report");
    }

    [TestMethod]
    public async Task PlanSeasonsAsync_ExistingByNaturalKey_StagesUnchangedAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string seriesId = await SeedExistingSeriesAsync(conn, "Avatar: The Last Airbender");
        await SeedExistingSeasonAsync(conn, seriesId, 1, "Book One", "Water");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            seasons: [BuildSeasonEntry(1, "Avatar: The Last Airbender", "Book One", "Water")]);

        Assert.ContainsSingle(actions.Where(a => a.EntityType == ImportActionEntityTypes.Season && a.ActionType.Parsed == ImportActionKind.Unchanged),
            "A Season matched by (SeriesId, Number) must report itself as Unchanged, not vanish from the report");
    }

    /// <summary>
    /// Person's own natural-key path differs in shape: <c>PersonEntryDto.Id</c> is required, so this
    /// path is reached only by a declared id that matched nothing while the Name did — the
    /// "not-yet-migrated row found only by Name" case #173 scoped out. Developer decision 2026-09-08:
    /// Person joins the other three; that boundary was never validated.
    /// </summary>
    [TestMethod]
    public async Task PlanPeopleAsync_ExistingByNameOnly_StagesUnchangedAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExplicitPersonAsync(conn, Guid.NewGuid().ToString("D"), "Ada Lovelace");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(Guid.NewGuid().ToString("D"), "Ada Lovelace")]);

        Assert.ContainsSingle(actions.Where(a => a.EntityType == ImportActionEntityTypes.Person && a.ActionType.Parsed == ImportActionKind.Unchanged),
            "A Person matched by Name after its declared id missed must report itself as Unchanged, not vanish from the report");
    }

    /// <summary>
    /// Row 29's control. Every assertion above is satisfied by a build that stages <c>Unchanged</c>
    /// unconditionally; this is the case that must NOT produce one, so "names every entity type" cannot
    /// be passed by naming them regardless of whether anything matched.
    /// </summary>
    [TestMethod]
    public async Task PlanSeasonsAsync_NoMatchAtAll_StagesAddNotUnchanged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExistingSeriesAsync(conn, "Avatar: The Last Airbender");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            seasons: [BuildSeasonEntry(3, "Avatar: The Last Airbender", "Book Three", "Fire")]);

        Assert.ContainsSingle(actions.Where(a => a.EntityType == ImportActionEntityTypes.Season && a.ActionType.Parsed == ImportActionKind.Add));
        Assert.IsEmpty(actions.Where(a => a.EntityType == ImportActionEntityTypes.Season && a.ActionType.Parsed == ImportActionKind.Unchanged),
            "A Season that matched nothing is an Add — never Unchanged");
    }

    // ── #373 step 10, row 30: the data-loss half ────────────────────────────
    // The natural-key lookups are ExecuteScalarAsync<Guid?> — they return an id and compare no field,
    // so a changed field on an id-less entry was discarded rather than merely unreported.

    [TestMethod]
    public async Task PlanSeasonsAsync_ExistingByNaturalKey_TitleDiffers_StagesModifyAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string seriesId = await SeedExistingSeriesAsync(conn, "Avatar: The Last Airbender");
        await SeedExistingSeasonAsync(conn, seriesId, 1, "Book One", "Water");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            seasons: [BuildSeasonEntry(1, "Avatar: The Last Airbender", "Book One: Water", "Water")]);

        Assert.ContainsSingle(actions.Where(a => a.EntityType == ImportActionEntityTypes.Season && a.ActionType.Parsed == ImportActionKind.Modify),
            "A corrected Season title on an id-less entry must stage a Modify, not be silently discarded");
    }

    [TestMethod]
    public async Task PlanSeriesAsync_ExistingByName_UniverseNameDiffers_StagesModifyAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExistingSeriesAsync(conn, "The Lord of the Rings");
        await SeedExistingUniverseAsync(conn, "Middle Earth");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            series: [BuildSeriesEntry("The Lord of the Rings", "Middle Earth")]);

        Assert.ContainsSingle(actions.Where(a => a.EntityType == ImportActionEntityTypes.Series && a.ActionType.Parsed == ImportActionKind.Modify),
            "A Series gaining its Universe link on an id-less entry must stage a Modify, not be silently discarded");
    }

    [TestMethod]
    public async Task PlanPeopleAsync_ExistingByNameOnly_DateOfBirthDiffers_StagesModifyAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExplicitPersonAsync(conn, Guid.NewGuid().ToString("D"), "Ada Lovelace", dateOfBirth: null, dateOfDeath: null);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(Guid.NewGuid().ToString("D"), "Ada Lovelace", "1815-12-10", null)]);

        Assert.ContainsSingle(actions.Where(a => a.EntityType == ImportActionEntityTypes.Person && a.ActionType.Parsed == ImportActionKind.Modify),
            "A Person's corrected dateOfBirth must stage a Modify once the id missed and the Name matched — #173's boundary was never validated");
    }

    [TestMethod]
    public async Task PlanUniverseAsync_NoMatchAtAll_StagesAddAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            universe: [BuildUniverseEntry("Middle Earth")]);

        ImportActionEntity universeAction = actions.Single(a => a.EntityType == "Universe");
        Assert.AreEqual(ImportActionKind.Add, universeAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, universeAction.Status.Parsed);
    }

    [TestMethod]
    public async Task PlanUniverseAsync_ExistingByName_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Universe (Id, Name, CompletenessStatus, DateCreated) VALUES (@Id, 'Middle Earth', 'Incomplete', @now)",
            new { Id = Guid.NewGuid().ToString("D").ToUpperInvariant(), now });

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            universe: [BuildUniverseEntry("Middle Earth")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Universe" && a.ActionType.Parsed != ImportActionKind.Unchanged), "Already exists by name — silently reused, no action staged");
    }

    /// <summary>
    /// #216 fix: Sql.Universe.SelectIdByName is now case-insensitive, matching #180's own
    /// Sql.Sources.SelectIdByTitleAndType precedent — a case-only difference must never stage a
    /// duplicate Universe.
    /// </summary>
    [TestMethod]
    public async Task PlanUniverseAsync_ExistingByName_DifferingCasing_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExistingUniverseAsync(conn, "Middle Earth");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            universe: [BuildUniverseEntry("MIDDLE EARTH")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Universe" && a.ActionType.Parsed != ImportActionKind.Unchanged), "Differing casing must still match the existing row by natural key, not stage a duplicate Add");
    }

    /// <summary>#163: Universe's own two-shape widening — explicit id present, matched by that id, name differs.</summary>
    [TestMethod]
    public async Task PlanUniverseAsync_ExplicitIdMatchFound_NameDiffers_StagesModifyAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = await SeedExistingUniverseAsync(conn, "Middle Earth");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            universe: [new UniverseEntryDto { Id = id, Name = "Middle-earth (corrected)" }]);

        ImportActionEntity universeAction = actions.Single(a => a.EntityType == "Universe");
        Assert.AreEqual(ImportActionKind.Modify, universeAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, universeAction.Status.Parsed);
        UniverseActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<UniverseActionPayloadDto>(universeAction.MergedFields!)!;
        Assert.AreEqual("Middle-earth (corrected)", merged.Name);
    }

    [TestMethod]
    public async Task PlanUniverseAsync_ReviewPolicy_MatchingRule_StagesDecidedNotPending()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = await SeedExistingUniverseAsync(conn, "Middle Earth");
        ConflictRuleLookup rules = new ConflictRuleLookup([
            new ConflictResolutionRule
            {
                EntityId = id,
                ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"Middle Earth"}"""),
                IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"Middle-earth (corrected)"}"""),
                Fields = [new ConflictResolutionFieldRule { Field = "name", Resolution = FieldResolutionChoice.Keep }],
            },
        ]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            universe: [new UniverseEntryDto { Id = id, Name = "Middle-earth (corrected)" }], conflictRules: rules);

        ImportActionEntity universeAction = actions.Single(a => a.EntityType == "Universe");
        Assert.AreEqual(ImportActionStatus.Decided, universeAction.Status.Parsed, "A matching rule must auto-resolve instead of leaving it Pending");
        UniverseActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<UniverseActionPayloadDto>(universeAction.MergedFields!)!;
        Assert.AreEqual("Middle Earth", merged.Name, "Keep must resolve to the existing side's value");
    }

    /// <summary>
    /// #181: proves the early-exit fix — a Custom rule fixing a field that's identical on both sides
    /// (nothing "changed" in the ordinary sense) must still get a chance to apply, not be silently
    /// skipped by the "unchanged — silent reuse" early exit that runs before the rule lookup.
    /// </summary>
    [TestMethod]
    public async Task PlanUniverseAsync_ReviewPolicy_CustomRuleOnUnchangedField_StillApplies()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = await SeedExistingUniverseAsync(conn, "Middle Earth");
        ConflictRuleLookup rules = new ConflictRuleLookup([
            new ConflictResolutionRule
            {
                EntityId = id,
                ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"Middle Earth"}"""),
                IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"Middle Earth"}"""),
                Fields = [new ConflictResolutionFieldRule { Field = "name", Resolution = FieldResolutionChoice.Custom, CustomValue = "Middle-earth" }],
            },
        ]);

        // Name is identical between existing and incoming — would hit the "unchanged" early exit
        // before #181, and never even reach the rule lookup.
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            universe: [new UniverseEntryDto { Id = id, Name = "Middle Earth" }], conflictRules: rules);

        ImportActionEntity universeAction = actions.Single(a => a.EntityType == "Universe");
        Assert.AreEqual(ImportActionStatus.Decided, universeAction.Status.Parsed, "The Custom rule must produce an action even though nothing 'changed' in the ordinary sense");
        UniverseActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<UniverseActionPayloadDto>(universeAction.MergedFields!)!;
        Assert.AreEqual("Middle-earth", merged.Name, "Custom must resolve to customValue, not either side's actual value");
    }

    /// <summary>
    /// #374, verification row 32 — proves the sink threads a real <c>Retirable</c> finding out of the
    /// planner, not just out of <c>ConflictRuleLookup</c> in isolation (already proven at step 4). A
    /// <c>Keep</c> rule whose incoming side has moved back into agreement with the existing value is
    /// exactly what <c>PlanUniverseAsync</c>'s own unconditional per-field rule check (the #181 fix
    /// above) reaches even though nothing "changed" in the ordinary sense.
    /// </summary>
    [TestMethod]
    public async Task PlanAsync_RetirableRule_IsCollectedIntoTheSink()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = await SeedExistingUniverseAsync(conn, "Middle Earth");
        ConflictRuleLookup rules = new ConflictRuleLookup([
            new ConflictResolutionRule
            {
                EntityId = id,
                ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"Middle Earth"}"""),
                // Recorded incoming ("Middle-earth (corrected)") disagreed with existing at authoring
                // time; the current incoming ("Middle Earth") has since moved back into agreement.
                IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"Middle-earth (corrected)"}"""),
                Fields = [new ConflictResolutionFieldRule { Field = "name", Resolution = FieldResolutionChoice.Keep }],
            },
        ]);
        List<RetirableRuleFinding> findings = [];

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            universe: [new UniverseEntryDto { Id = id, Name = "Middle Earth" }], conflictRules: rules, retirableRuleFindings: findings);

        Assert.ContainsSingle(findings);
        Assert.AreEqual("Universe", findings[0].EntityType);
        Assert.AreEqual(id, findings[0].EntityId);
        Assert.AreEqual("name", findings[0].Field);

        ImportActionEntity universeAction = actions.Single(a => a.EntityType == "Universe");
        Assert.AreEqual(ImportActionStatus.Stale, universeAction.Status.Parsed, "Retirable still holds the action for review exactly like Stale — only the remedy reported differs (delete the rule, not re-author it)");
    }

    /// <summary>The control for <see cref="PlanAsync_RetirableRule_IsCollectedIntoTheSink"/> — an ordinary Stale rule must never be collected as retirable.</summary>
    [TestMethod]
    public async Task PlanAsync_StaleRule_IsNotCollectedAsRetirable()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = await SeedExistingUniverseAsync(conn, "Middle Earth");
        ConflictRuleLookup rules = new ConflictRuleLookup([
            new ConflictResolutionRule
            {
                EntityId = id,
                ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"Middle Earth"}"""),
                IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"A name that no longer matches anything"}"""),
                Fields = [new ConflictResolutionFieldRule { Field = "name", Resolution = FieldResolutionChoice.Keep }],
            },
        ]);
        List<RetirableRuleFinding> findings = [];

        await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            universe: [new UniverseEntryDto { Id = id, Name = "Middle-earth (corrected)" }], conflictRules: rules, retirableRuleFindings: findings);

        Assert.IsEmpty(findings, "A genuinely stale rule (incoming side moved since authoring) must never be reported as retirable");
    }

    [TestMethod]
    public async Task PlanSeriesAsync_NoMatchAtAll_StagesAddAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            series: [BuildSeriesEntry("The Lord of the Rings")]);

        ImportActionEntity seriesAction = actions.Single(a => a.EntityType == "Series");
        Assert.AreEqual(ImportActionKind.Add, seriesAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, seriesAction.Status.Parsed);
    }

    /// <summary>
    /// #216 fix: Sql.Series.SelectIdByName is now case-insensitive, matching #180's own
    /// Sql.Sources.SelectIdByTitleAndType precedent — a case-only difference must never stage a
    /// duplicate Series.
    /// </summary>
    [TestMethod]
    public async Task PlanSeriesAsync_ExistingByName_DifferingCasing_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExistingSeriesAsync(conn, "The Lord of the Rings");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            series: [BuildSeriesEntry("THE LORD OF THE RINGS")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Series" && a.ActionType.Parsed != ImportActionKind.Unchanged), "Differing casing must still match the existing row by natural key, not stage a duplicate Add");
    }

    /// <summary>#163: Series' own two-shape widening — explicit id present, matched by that id, name differs.</summary>
    [TestMethod]
    public async Task PlanSeriesAsync_ExplicitIdMatchFound_NameDiffers_StagesModifyAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = await SeedExistingSeriesAsync(conn, "The Hobbit");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            series: [new SeriesEntryDto { Id = id, Name = "The Hobbit Trilogy" }]);

        ImportActionEntity seriesAction = actions.Single(a => a.EntityType == "Series");
        Assert.AreEqual(ImportActionKind.Modify, seriesAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, seriesAction.Status.Parsed);
        SeriesActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<SeriesActionPayloadDto>(seriesAction.MergedFields!)!;
        Assert.AreEqual("The Hobbit Trilogy", merged.Name);
    }

    /// <summary>#163: Series' own two-shape widening — explicit id present, matched by that id, universeId differs.</summary>
    [TestMethod]
    public async Task PlanSeriesAsync_ExplicitIdMatchFound_UniverseIdDiffers_StagesModifyAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string originalUniverseId = await SeedExistingUniverseAsync(conn, "Middle Earth");
        string newUniverseId      = await SeedExistingUniverseAsync(conn, "The Shire Cinematic Universe");
        string id = await SeedExistingSeriesAsync(conn, "The Hobbit", universeId: originalUniverseId);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            series: [new SeriesEntryDto { Id = id, Name = "The Hobbit", UniverseName = "The Shire Cinematic Universe" }]);

        ImportActionEntity seriesAction = actions.Single(a => a.EntityType == "Series");
        Assert.AreEqual(ImportActionKind.Modify, seriesAction.ActionType.Parsed);
        SeriesActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<SeriesActionPayloadDto>(seriesAction.MergedFields!)!;
        Assert.AreEqual(newUniverseId.ToUpperInvariant(), merged.UniverseId?.ToUpperInvariant());
    }

    [TestMethod]
    public async Task PlanSeriesAsync_ReviewPolicy_MatchingRule_StagesDecidedNotPending()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = await SeedExistingSeriesAsync(conn, "The Hobbit");
        ConflictRuleLookup rules = new ConflictRuleLookup([
            new ConflictResolutionRule
            {
                EntityId = id,
                ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"The Hobbit"}"""),
                IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"The Hobbit Trilogy"}"""),
                Fields = [new ConflictResolutionFieldRule { Field = "name", Resolution = FieldResolutionChoice.Keep }],
            },
        ]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            series: [new SeriesEntryDto { Id = id, Name = "The Hobbit Trilogy" }], conflictRules: rules);

        ImportActionEntity seriesAction = actions.Single(a => a.EntityType == "Series");
        Assert.AreEqual(ImportActionStatus.Decided, seriesAction.Status.Parsed, "A matching rule must auto-resolve instead of leaving it Pending");
        SeriesActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<SeriesActionPayloadDto>(seriesAction.MergedFields!)!;
        Assert.AreEqual("The Hobbit", merged.Name, "Keep must resolve to the existing side's value");
    }

    [TestMethod]
    public async Task PlanSeriesAsync_UniverseNameResolvesToSameBatchUniverseAdd_PayloadCarriesUniverseId()
    {
        using SqliteConnection conn = await OpenConnectionAsync();

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            series: [BuildSeriesEntry("The Lord of the Rings", universeName: "Middle Earth")],
            universe: [BuildUniverseEntry("Middle Earth")]);

        ImportActionEntity universeAction = actions.Single(a => a.EntityType == "Universe");
        ImportActionEntity seriesAction   = actions.Single(a => a.EntityType == "Series");
        SeriesActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<SeriesActionPayloadDto>(seriesAction.IncomingValue!)!;
        Assert.AreEqual(universeAction.EntityId, payload.UniverseId, "Series' Add payload must carry the same-batch Universe Add's own stable id");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_SeriesNameResolvesToSameBatchSeriesAdd_PayloadCarriesSeriesId()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceFileId = "c8111111-1111-4111-8111-111111111111";

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(sourceFileId, title: "A Brand New Film", seriesName: "The Lord of the Rings")],
            series: [BuildSeriesEntry("The Lord of the Rings")]);

        ImportActionEntity seriesAction = actions.Single(a => a.EntityType == "Series");
        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        SourceActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.IncomingValue!)!;
        Assert.AreEqual(seriesAction.EntityId, payload.SeriesId, "Source's Add payload must carry the same-batch Series Add's own stable id");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_SeriesNameChanged_StagesModifyAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "c9111111-1111-4111-8111-111111111111";
        string seriesId = await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", seriesId: null);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(id, title: "Casablanca", seriesName: "The Hobbit")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, sourceAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, sourceAction.Status.Parsed);
        SourceActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.MergedFields!)!;
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
        using SqliteConnection conn = await OpenConnectionAsync();
        string seriesId = await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, "cc111111-1111-4111-8111-111111111111", title: "Casablanca", date: "1942", seriesId: null);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: "The Hobbit")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, sourceAction.ActionType.Parsed, "A natural-key match must stage a Modify, not be silently skipped");
        SourceActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.MergedFields!)!;
        Assert.AreEqual(seriesId, merged.SeriesId);
    }

    /// <summary>
    /// #181: found live via Docker during #217's own verification — a Source established implicitly by
    /// one bundled file (e.g. quotinator-curated.json, via a quote) and later enriched with a Series
    /// link by another (quotinator-series-universe.json's own sources[] entry) is a genuine,
    /// expected cross-file Modify that previously had no way to auto-resolve under Review, since
    /// PlanSourcesAsync was one of the sites deliberately left unwired pending an observed conflict.
    /// </summary>
    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_ReviewPolicy_NoMatchingRule_StagesPending()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, "ce111111-1111-4111-8111-111111111111", title: "Casablanca", date: "1942", seriesId: null);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: "The Hobbit")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Pending, sourceAction.Status.Parsed, "No rule exists for this Source's seriesId enrichment under Review — regression guard matching pre-#181 behaviour for this site");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_ReviewPolicy_MatchingRule_StagesDecided()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string hobbitSeriesId = await SeedExistingSeriesAsync(conn, "The Hobbit");
        string sourceId = "cf111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(conn, sourceId, title: "Casablanca", date: "1942", seriesId: null);
        ConflictRuleLookup rules = new ConflictRuleLookup([
            new ConflictResolutionRule
            {
                EntityId = sourceId,
                ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"seriesId":null}"""),
                IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>($$"""{"seriesId":"{{hobbitSeriesId}}"}"""),
                Fields = [new ConflictResolutionFieldRule { Field = "seriesId", Resolution = FieldResolutionChoice.Replace }],
            },
        ]);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: "The Hobbit")], conflictRules: rules);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Decided, sourceAction.Status.Parsed, "A matching rule must auto-resolve the Source's seriesId enrichment instead of leaving it Pending");
        SourceActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.MergedFields!)!;
        Assert.IsNotNull(merged.SeriesId);
    }

    /// <summary>
    /// The core of #180's second design point: an entry that omits `date` must never reset the
    /// existing row's date. The resolved payload feeds Sql.Sources.UpdateFieldsById, which writes
    /// Date unconditionally — so a null here would silently wipe a real date on every apply.
    /// </summary>
    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_OmittedDate_PreservesExistingDate()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, "cd111111-1111-4111-8111-111111111111", title: "Casablanca", date: "1942", seriesId: null);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: "The Hobbit")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        SourceActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.MergedFields!)!;
        Assert.AreEqual("1942", merged.Date, "An omitted date must carry the existing row's value through, never null it out");
        Assert.AreEqual("Casablanca", merged.Title, "Title is the lookup key on this path — never a correction");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_NaturalKeyMatch_NoSeriesName_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExplicitSourceAsync(conn, "ce111111-1111-4111-8111-111111111111", title: "Casablanca", date: "1942");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: null)]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source" && a.ActionType.Parsed != ImportActionKind.Unchanged), "Nothing to enrich and nothing to correct — unchanged from #162's own natural-key behaviour");
    }

    /// <summary>
    /// #175/developer decision (2026-07-24): Sql.Sources.SelectIdByTitleAndType/
    /// SelectExistingByTitleAndType are now case-insensitive — any input from an import file must
    /// match regardless of casing, so classifying an entry as new-vs-existing carries minimal
    /// friction and never risks a case-only duplicate.
    /// </summary>
    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_DifferingCasing_MatchesExistingNaturalKey()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExplicitSourceAsync(conn, "d1111111-1111-4111-8111-111111111111", title: "Casablanca", type: "Movie");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildEnrichmentEntry(title: "CASABLANCA", type: Core.Enums.QuoteType.Movie, seriesName: null)]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source" && a.ActionType.Parsed != ImportActionKind.Unchanged), "Differing casing must still match the existing row by natural key, not stage a duplicate Add");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_AlreadyTagged_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string seriesId = await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, "cf111111-1111-4111-8111-111111111111", title: "Casablanca", seriesId: seriesId);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: "The Hobbit")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source" && a.ActionType.Parsed != ImportActionKind.Unchanged), "Already points at this Series — a true no-op, nothing staged even under Review");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_NoMatchAtAll_StagesAddWithComputedId()
    {
        using SqliteConnection conn = await OpenConnectionAsync();

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildEnrichmentEntry(title: "A Brand New Film", seriesName: null)]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Add, sourceAction.ActionType.Parsed);
        Assert.AreEqual(Quotinator.Core.Import.EntityIdentity.SourceId("A Brand New Film", "Movie"), sourceAction.EntityId,
            "With no explicit id in the file, an Add uses the EntityIdentity-derived stable id — the same one ResolveSourceAsync would compute for a quote referencing this title");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_CompleteStatus_SeriesNameSet_StagesBlocked()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, "d0111111-1111-4111-8111-111111111111", title: "Casablanca", completenessStatus: "Complete", seriesId: null);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: "The Hobbit")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Blocked, sourceAction.Status.Parsed, "CompletenessGuard applies on the natural-key path too — a Complete row is never silently enriched");
    }

    /// <summary>#180 spec requirement 3: a genuine SeriesId disagreement under Review policy stages Pending, never silently resolves.</summary>
    [TestMethod]
    public async Task PlanSourcesAsync_ReviewPolicy_SeriesNameChanged_StagesPendingNotAutoResolved()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "cb111111-1111-4111-8111-111111111111";
        string originalSeriesId = await SeedExistingSeriesAsync(conn, "Original Series");
        await SeedExistingSeriesAsync(conn, "Edited Series");
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", seriesId: originalSeriesId);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            sources: [BuildSourceEntry(id, title: "Casablanca", seriesName: "Edited Series")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, sourceAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Pending, sourceAction.Status.Parsed, "A genuine SeriesId disagreement under review policy must stage Pending, not silently resolve");
        Assert.IsNull(sourceAction.MergedFields, "Nothing is resolved yet for a Pending action");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_CompleteStatus_SeriesNameChanged_StagesBlockedNotModify()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "ca111111-1111-4111-8111-111111111111";
        await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", completenessStatus: "Complete", seriesId: null);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(id, title: "Casablanca", seriesName: "The Hobbit")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Blocked, sourceAction.Status.Parsed, "A Complete row must never silently accept a Modify, including a SeriesId-only change");
        Assert.IsNull(sourceAction.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_IdMatchFound_TitleDiffers_StagesModifyAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "c1111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(id, title: "Casablanca (1942)")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
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
        using SqliteConnection conn = await OpenConnectionAsync();
        string uppercaseId = "CB111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(conn, uppercaseId, title: "Casablanca");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(uppercaseId.ToLowerInvariant(), title: "Casablanca (Corrected)")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, sourceAction.ActionType.Parsed, "Must match the existing row by id (case-insensitively), not stage a duplicate Add");
        Assert.AreEqual(ImportActionStatus.Decided, sourceAction.Status.Parsed);
    }

    [TestMethod]
    public async Task PlanSourcesAsync_IdMatchFound_NothingChanged_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "c2111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", type: "Movie", date: "1942");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(id, title: "Casablanca", type: Core.Enums.QuoteType.Movie, date: "1942")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source" && a.ActionType.Parsed != ImportActionKind.Unchanged), "Nothing differs — silent reuse, no action staged");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoIdMatch_FallsBackToNaturalKey_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        // A pre-existing row found only by natural key (Title+Type) — never declared an explicit id before.
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("INSERT INTO Quotinator_Source (Id, Title, Type, DateCreated) VALUES (@Id, 'Casablanca', 'Movie', @now)",
            new { Id = Guid.NewGuid(), now });

        string newFileId = "c3111111-1111-4111-8111-111111111111";
        // #190: date must be passed explicitly as null here — BuildSourceEntry's own default ("1942")
        // would otherwise now genuinely take effect on the natural-key path (requirement 6's
        // liberalization), which is a different, separately-tested scenario, not what this test means
        // to exercise (nothing about this entry differs from the existing row at all).
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(newFileId, title: "Casablanca", type: Core.Enums.QuoteType.Movie, date: null)]);

        Assert.IsEmpty(actions.Where(a => a.ActionType.Parsed != ImportActionKind.Unchanged),
            "Not-yet-migrated row found via natural key — no re-keying, nothing staged (#162 scope boundary)");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoMatchAtAll_StagesAddWithFileId()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string newFileId = "c4111111-1111-4111-8111-111111111111";

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(newFileId, title: "A Brand New Film")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Add, sourceAction.ActionType.Parsed);
        // #209/#210: Add uses the file's own declared id, canonicalized at capture (ADR 012) — not an
        // EntityIdentity-derived stable id, and no longer the file's raw casing verbatim.
        Assert.AreEqual(newFileId, sourceAction.EntityId);
    }

    [TestMethod]
    public async Task PlanSourcesAsync_CompleteStatus_StagesBlockedNotModify()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "c5111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", completenessStatus: "Complete");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(id, title: "Casablanca (Corrected)")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Blocked, sourceAction.Status.Parsed, "A Complete row must never silently accept a Modify");
        Assert.IsNull(sourceAction.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_CompleteSource_SkipPolicy_DoesNotBlock()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "c5211111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", completenessStatus: "Complete");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Skip,
            sources: [BuildSourceEntry(id, title: "Casablanca (Corrected)")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Decided, sourceAction.Status.Parsed, "Skip's resolved value always equals the existing row — nothing would change, so a Complete row must never block");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_QuoteReferencesExplicitlyDeclaredSource_ResolvesToItsId()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string newFileId = "c6111111-1111-4111-8111-111111111111";
        SourceQuoteDto quote = BuildQuote("c7111111-1111-4111-8111-111111111111", source: "A Brand New Film");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(newFileId, title: "A Brand New Film")]);

        Assert.ContainsSingle(a => a.EntityType == "Source", actions, "Only one Source Add — the quote must resolve to the same row the sources[] section staged, not a second one");
        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        QuoteActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        // #209/#210: the quote resolves to the canonicalized form of the file's declared id (ADR 012).
        Assert.AreEqual(newFileId, payload.SourceId);
    }

    // ── #171: PlanStageDirectionsAsync ───────────────────────────────────────

    private static SourceStageDirectionDto BuildStageDirectionEntry(string id, string text = "A shot rings out.", string? imageUrl = null) => new()
    {
        Id       = id,
        Text     = text,
        ImageUrl = imageUrl,
    };

    private static async Task SeedExplicitStageDirectionAsync(SqliteConnection conn, string id, string text = "A shot rings out.", string? imageUrl = null, string completenessStatus = "Incomplete")
    {
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_StageDirection (Id, Text, ImageUrl, CompletenessStatus, DateCreated) VALUES (@Id, @Text, @ImageUrl, @CompletenessStatus, @now)",
            new { Id = id, Text = text, ImageUrl = imageUrl, CompletenessStatus = completenessStatus, now });
    }

    [TestMethod]
    public async Task PlanStageDirectionsAsync_IdMatchFound_TextDiffers_StagesModifyAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "d1111111-1111-4111-8111-111111111111";
        await SeedExplicitStageDirectionAsync(conn, id, text: "A shot rings out.");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            stageDirections: [BuildStageDirectionEntry(id, text: "A single shot rings out in the distance.")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "StageDirection");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed);
        Assert.IsNotNull(action.MergedFields);
    }

    [TestMethod]
    public async Task PlanStageDirectionsAsync_IdMatchFound_NothingChanged_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "d2111111-1111-4111-8111-111111111111";
        await SeedExplicitStageDirectionAsync(conn, id, text: "A shot rings out.", imageUrl: "https://example.com/still.jpg");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            stageDirections: [BuildStageDirectionEntry(id, text: "A shot rings out.", imageUrl: "https://example.com/still.jpg")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "StageDirection" && a.ActionType.Parsed != ImportActionKind.Unchanged), "Nothing differs — silent reuse, no action staged");
    }

    [TestMethod]
    public async Task PlanStageDirectionsAsync_CompleteStatus_StagesBlockedNotModify()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "d3111111-1111-4111-8111-111111111111";
        await SeedExplicitStageDirectionAsync(conn, id, text: "A shot rings out.", completenessStatus: "Complete");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            stageDirections: [BuildStageDirectionEntry(id, text: "A different action entirely.")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "StageDirection");
        Assert.AreEqual(ImportActionStatus.Blocked, action.Status.Parsed, "A Complete row must never silently accept a Modify");
        Assert.IsNull(action.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanStageDirectionsAsync_CompleteStatus_SkipPolicy_DoesNotBlock()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "d4111111-1111-4111-8111-111111111111";
        await SeedExplicitStageDirectionAsync(conn, id, text: "A shot rings out.", completenessStatus: "Complete");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Skip,
            stageDirections: [BuildStageDirectionEntry(id, text: "A different action entirely.")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "StageDirection");
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed, "Skip's resolved value always equals the existing row — nothing would change, so a Complete row must never block");
    }

    // ── #172: PlanSoundCuesAsync ──────────────────────────────────────────────

    private static SourceSoundCueDto BuildSoundCueEntry(string id, string text = "Distant thunder.", string? soundFileUrl = null, string? imageUrl = null) => new()
    {
        Id           = id,
        Text         = text,
        SoundFileUrl = soundFileUrl,
        ImageUrl     = imageUrl,
    };

    private static async Task SeedExplicitSoundCueAsync(SqliteConnection conn, string id, string text = "Distant thunder.", string? soundFileUrl = null, string? imageUrl = null, string completenessStatus = "Incomplete")
    {
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_SoundCue (Id, Text, SoundFileUrl, ImageUrl, CompletenessStatus, DateCreated) VALUES (@Id, @Text, @SoundFileUrl, @ImageUrl, @CompletenessStatus, @now)",
            new { Id = id, Text = text, SoundFileUrl = soundFileUrl, ImageUrl = imageUrl, CompletenessStatus = completenessStatus, now });
    }

    [TestMethod]
    public async Task PlanSoundCuesAsync_IdMatchFound_TextDiffers_StagesModifyAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "d5111111-1111-4111-8111-111111111111";
        await SeedExplicitSoundCueAsync(conn, id, text: "Distant thunder.");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            soundCues: [BuildSoundCueEntry(id, text: "Rolling thunder in the distance.")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "SoundCue");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed);
        Assert.IsNotNull(action.MergedFields);
    }

    [TestMethod]
    public async Task PlanSoundCuesAsync_IdMatchFound_NothingChanged_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "d6111111-1111-4111-8111-111111111111";
        await SeedExplicitSoundCueAsync(conn, id, text: "Distant thunder.", soundFileUrl: "https://example.com/thunder.mp3");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            soundCues: [BuildSoundCueEntry(id, text: "Distant thunder.", soundFileUrl: "https://example.com/thunder.mp3")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "SoundCue" && a.ActionType.Parsed != ImportActionKind.Unchanged), "Nothing differs — silent reuse, no action staged");
    }

    [TestMethod]
    public async Task PlanSoundCuesAsync_CompleteStatus_StagesBlockedNotModify()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "d7111111-1111-4111-8111-111111111111";
        await SeedExplicitSoundCueAsync(conn, id, text: "Distant thunder.", completenessStatus: "Complete");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            soundCues: [BuildSoundCueEntry(id, text: "A completely different sound.")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "SoundCue");
        Assert.AreEqual(ImportActionStatus.Blocked, action.Status.Parsed, "A Complete row must never silently accept a Modify");
        Assert.IsNull(action.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanSoundCuesAsync_CompleteStatus_SkipPolicy_DoesNotBlock()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "d8111111-1111-4111-8111-111111111111";
        await SeedExplicitSoundCueAsync(conn, id, text: "Distant thunder.", completenessStatus: "Complete");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Skip,
            soundCues: [BuildSoundCueEntry(id, text: "A completely different sound.")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "SoundCue");
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed, "Skip's resolved value always equals the existing row — nothing would change, so a Complete row must never block");
    }

    // ── #173: PlanPeopleAsync ─────────────────────────────────────────────────

    private static PersonEntryDto BuildPersonEntry(string id, string name = "Ada Lovelace", string? dateOfBirth = "1815-12-10", string? dateOfDeath = "1852-11-27") => new()
    {
        Id          = id,
        Name        = name,
        DateOfBirth = dateOfBirth,
        DateOfDeath = dateOfDeath,
    };

    private static async Task SeedExplicitPersonAsync(SqliteConnection conn, string id, string name = "Ada Lovelace", string? dateOfBirth = "1815-12-10", string? dateOfDeath = "1852-11-27", string completenessStatus = "Incomplete")
    {
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Person (Id, Name, DateOfBirth, DateOfDeath, CompletenessStatus, DateCreated) VALUES (@Id, @Name, @DateOfBirth, @DateOfDeath, @CompletenessStatus, @now)",
            new { Id = id, Name = name, DateOfBirth = dateOfBirth, DateOfDeath = dateOfDeath, CompletenessStatus = completenessStatus, now });
    }

    [TestMethod]
    public async Task PlanPeopleAsync_IdMatchFound_NameDiffers_StagesModifyAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e1111111-1111-4111-8111-111111111173";
        await SeedExplicitPersonAsync(conn, id, name: "Ada Lovelace");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(id, name: "Augusta Ada King, Countess of Lovelace")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed);
        Assert.IsNotNull(action.MergedFields);
    }

    [TestMethod]
    public async Task PlanPeopleAsync_IdMatchFound_NothingChanged_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e2111111-1111-4111-8111-111111111173";
        await SeedExplicitPersonAsync(conn, id, name: "Ada Lovelace", dateOfBirth: "1815-12-10", dateOfDeath: "1852-11-27");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(id, name: "Ada Lovelace", dateOfBirth: "1815-12-10", dateOfDeath: "1852-11-27")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Person" && a.ActionType.Parsed != ImportActionKind.Unchanged), "Nothing differs — silent reuse, no action staged");
    }

    /// <summary>
    /// A not-yet-migrated row found only by Name keeps its own id — the file's declared id never
    /// re-keys it. That half of #173's boundary stands. The other half — that nothing at all is staged
    /// — was retired by the developer's 2026-09-08 decision (#373 step 10): the correction the file
    /// carries is now applied rather than discarded, so this stages a Modify against the *existing*
    /// row's id.
    /// </summary>
    [TestMethod]
    public async Task PlanPeopleAsync_NoIdMatch_FallsBackToNaturalKey_StagesAgainstTheExistingIdNotTheFilesOwn()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        // A pre-existing row found only by natural key (Name) — never declared an explicit id before.
        string existingId = Guid.NewGuid().ToString("D");
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("INSERT INTO Quotinator_Person (Id, Name, DateCreated) VALUES (@Id, 'Ada Lovelace', @now)",
            new { Id = existingId, now });

        string newFileId = "e3111111-1111-4111-8111-111111111173";
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(newFileId, name: "Ada Lovelace")]);

        ImportActionEntity personAction = actions.Single(a => a.EntityType == ImportActionEntityTypes.Person);
        Assert.AreEqual(existingId.ToLowerInvariant(), personAction.EntityId.ToLowerInvariant(),
            "No re-keying — the action targets the row that already exists, never the id the file declared");
        Assert.IsEmpty(actions.Where(a => a.ActionType.Parsed == ImportActionKind.Add),
            "A natural-key match is never a duplicate Add");
    }

    /// <summary>
    /// #216 fix: Sql.People.SelectIdByName is now case-insensitive, matching #180's own
    /// Sql.Sources.SelectIdByTitleAndType precedent — a case-only difference must still find the
    /// existing row via natural key, not stage a duplicate Add. That is what this test proves, and it
    /// is unaffected by #373 step 10; the assertion is narrowed from "no action at all" to "no Add"
    /// because the natural-key path now stages the correction it used to discard.
    /// </summary>
    [TestMethod]
    public async Task PlanPeopleAsync_NoIdMatch_DifferingCasing_FallsBackToNaturalKey_StagesNoDuplicateAdd()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("INSERT INTO Quotinator_Person (Id, Name, DateCreated) VALUES (@Id, 'Ada Lovelace', @now)",
            new { Id = Guid.NewGuid(), now });

        string newFileId = "e3211111-1111-4111-8111-111111111173";
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(newFileId, name: "ADA LOVELACE")]);

        Assert.IsEmpty(actions.Where(a => a.ActionType.Parsed == ImportActionKind.Add),
            "Differing casing must still match the existing row via natural key, not stage a duplicate Add");
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Person"),
            "The positive control: still exactly one Person row, so the match was real and not a second insert");
    }

    [TestMethod]
    public async Task PlanPeopleAsync_CompleteStatus_StagesBlockedNotModify()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e4111111-1111-4111-8111-111111111173";
        await SeedExplicitPersonAsync(conn, id, name: "Ada Lovelace", completenessStatus: "Complete");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(id, name: "A completely different name")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(ImportActionStatus.Blocked, action.Status.Parsed, "A Complete row must never silently accept a Modify");
        Assert.IsNull(action.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanPeopleAsync_CompleteStatus_SkipPolicy_DoesNotBlock()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e5111111-1111-4111-8111-111111111173";
        await SeedExplicitPersonAsync(conn, id, name: "Ada Lovelace", completenessStatus: "Complete");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Skip,
            people: [BuildPersonEntry(id, name: "A completely different name")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed, "Skip's resolved value always equals the existing row — nothing would change, so a Complete row must never block");
    }

    // ── #175: PlanCharactersAsync (widened schema — id optional, sourceTitle/sourceType required) ──

    private static CharacterEntryDto BuildCharacterEntry(string? id, string name = "Gandalf", string sourceTitle = "Existing Film", Core.Enums.QuoteType sourceType = Core.Enums.QuoteType.Movie) => new()
    {
        Id          = id,
        Name        = name,
        SourceTitle = sourceTitle,
        SourceType  = sourceType,
    };

    [TestMethod]
    public async Task PlanCharactersAsync_IdMatchFound_NameDiffers_StagesModifyAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = await SeedSourceAsync(conn, "Existing Film");
        string characterId = await SeedGlobalCharacterAsync(conn, "Gandalf", sourceId, "Movie");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(characterId, name: "Gandalf the Grey")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Character");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed);
        Assert.IsNotNull(action.MergedFields);
    }

    [TestMethod]
    public async Task PlanCharactersAsync_IdMatchFound_NothingChanged_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = await SeedSourceAsync(conn, "Existing Film");
        string characterId = await SeedGlobalCharacterAsync(conn, "Gandalf", sourceId, "Movie");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(characterId, name: "Gandalf")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Character" && a.ActionType.Parsed != ImportActionKind.Unchanged), "Nothing differs — silent reuse, no action staged");
    }

    [TestMethod]
    public async Task PlanCharactersAsync_IdDoesNotMatch_FallsBackToSameSourceCandidate_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = await SeedSourceAsync(conn, "Existing Film");
        await SeedGlobalCharacterAsync(conn, "Gandalf", sourceId, "Movie");
        string bogusId = "aaaaaaaa-1111-4111-8111-111111111111";

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(bogusId, name: "Gandalf", sourceTitle: "Existing Film", sourceType: Core.Enums.QuoteType.Movie)]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Character" && a.ActionType.Parsed != ImportActionKind.Unchanged), "A declared id that matches nothing must fall back to ADR 013's real matching algorithm, same as PlanSourcesAsync's own id-not-found fallback");
    }

    [TestMethod]
    public async Task PlanCharactersAsync_NoIdMatch_SeriesScopedCandidateFound_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string seriesId = await SeedExistingSeriesAsync(conn, "LOTR Trilogy");
        string source1Id = await SeedSourceAsync(conn, "The Fellowship of the Ring", seriesId: seriesId);
        await SeedGlobalCharacterAsync(conn, "Aragorn", source1Id, "Movie");
        await SeedSourceAsync(conn, "The Two Towers", seriesId: seriesId);

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(null, name: "Aragorn", sourceTitle: "The Two Towers", sourceType: Core.Enums.QuoteType.Movie)]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Character" && a.ActionType.Parsed != ImportActionKind.Unchanged), "A Series-scoped cross-Source candidate must be reused directly, matching ResolveCharacterAsync's own behaviour");
    }

    [TestMethod]
    public async Task PlanCharactersAsync_NoIdMatch_NoCandidateFound_StagesAddAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        await SeedSourceAsync(conn, "A Brand New Film");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(null, name: "A Brand New Character", sourceTitle: "A Brand New Film", sourceType: Core.Enums.QuoteType.Movie)]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Character");
        Assert.AreEqual(ImportActionKind.Add, action.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed);
    }

    [TestMethod]
    public async Task PlanCharactersAsync_NoIdMatch_SourceDoesNotExistYet_StagesBothSourceAndCharacterAdds()
    {
        using SqliteConnection conn = await OpenConnectionAsync();

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(null, name: "A Brand New Character", sourceTitle: "A Never-Before-Seen Film", sourceType: Core.Enums.QuoteType.Movie)]);

        Assert.ContainsSingle(a => a.EntityType == "Source", actions, "The referenced Source must be resolved/created too, same as a quote's own ResolveSourceAsync");
        ImportActionEntity characterAction = actions.Single(a => a.EntityType == "Character");
        Assert.AreEqual(ImportActionKind.Add, characterAction.ActionType.Parsed);
    }

    [TestMethod]
    public async Task PlanCharactersAsync_CompleteStatus_StagesBlockedNotModify()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = await SeedSourceAsync(conn, "Existing Film");
        string characterId = await SeedGlobalCharacterAsync(conn, "Gandalf", sourceId, "Movie");
        await conn.ExecuteAsync("UPDATE Quotinator_Character SET CompletenessStatus = 'Complete' WHERE Id = @id", new { id = characterId });

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(characterId, name: "A completely different name")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Character");
        Assert.AreEqual(ImportActionStatus.Blocked, action.Status.Parsed, "A Complete row must never silently accept a Modify");
        Assert.IsNull(action.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanCharactersAsync_CompleteStatus_SkipPolicy_DoesNotBlock()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = await SeedSourceAsync(conn, "Existing Film");
        string characterId = await SeedGlobalCharacterAsync(conn, "Gandalf", sourceId, "Movie");
        await conn.ExecuteAsync("UPDATE Quotinator_Character SET CompletenessStatus = 'Complete' WHERE Id = @id", new { id = characterId });

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Skip,
            characters: [BuildCharacterEntry(characterId, name: "A completely different name")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Character");
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed, "Skip's resolved value always equals the existing row — nothing would change, so a Complete row must never block");
    }

    // ── #176: PlanConversationsAsync ─────────────────────────────────────────

    private static SourceConversationDto BuildConversationEntry(string id, string? description = "A tense standoff.") => new()
    {
        Id          = id,
        Description = description,
        Lines       = [],
    };

    private static async Task SeedExplicitConversationAsync(SqliteConnection conn, string id, string? description = "A tense standoff.", string completenessStatus = "Incomplete")
    {
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Conversation (Id, Description, CompletenessStatus, DateCreated) VALUES (@Id, @Description, @CompletenessStatus, @now)",
            new { Id = id, Description = description, CompletenessStatus = completenessStatus, now });
    }

    [TestMethod]
    public async Task PlanConversationsAsync_IdMatchFound_DescriptionDiffers_StagesModifyAction()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "d9111111-1111-4111-8111-111111111176";
        await SeedExplicitConversationAsync(conn, id, description: "A tense standoff.");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            conversations: [BuildConversationEntry(id, description: "A tense standoff in the saloon.")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Conversation");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed);
        Assert.IsNotNull(action.MergedFields);
    }

    [TestMethod]
    public async Task PlanConversationsAsync_IdMatchFound_NothingChanged_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "da111111-1111-4111-8111-111111111176";
        await SeedExplicitConversationAsync(conn, id, description: "A tense standoff.");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            conversations: [BuildConversationEntry(id, description: "A tense standoff.")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Conversation" && a.ActionType.Parsed != ImportActionKind.Unchanged), "Nothing differs — silent reuse, no action staged");
    }

    [TestMethod]
    public async Task PlanConversationsAsync_IdMatchFound_LinesNeverDiffed()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "db111111-1111-4111-8111-111111111176";
        await SeedExplicitConversationAsync(conn, id, description: "A tense standoff.");

        SourceConversationDto entry = new SourceConversationDto
        {
            Id          = id,
            Description = "A tense standoff in the saloon.",
            Lines       = [new SourceConversationLineDto { Order = 0, Type = Core.Enums.ConversationLineType.Quote, QuoteId = "11111111-1111-4111-8111-111111111111" }],
        };
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, conversations: [entry]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Conversation");
        ConversationActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<ConversationActionPayloadDto>(action.MergedFields!)!;
        Assert.IsEmpty(merged.Lines, "Lines are never read or included in a Modify payload — out of scope for this issue");
    }

    [TestMethod]
    public async Task PlanConversationsAsync_CompleteStatus_StagesBlockedNotModify()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "dc111111-1111-4111-8111-111111111176";
        await SeedExplicitConversationAsync(conn, id, description: "A tense standoff.", completenessStatus: "Complete");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            conversations: [BuildConversationEntry(id, description: "A completely different scene.")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Conversation");
        Assert.AreEqual(ImportActionStatus.Blocked, action.Status.Parsed, "A Complete row must never silently accept a Modify");
        Assert.IsNull(action.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanConversationsAsync_CompleteStatus_SkipPolicy_DoesNotBlock()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "dd111111-1111-4111-8111-111111111176";
        await SeedExplicitConversationAsync(conn, id, description: "A tense standoff.", completenessStatus: "Complete");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Skip,
            conversations: [BuildConversationEntry(id, description: "A completely different scene.")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Conversation");
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed, "Skip's resolved value always equals the existing row — nothing would change, so a Complete row must never block");
    }

    // ── #209: canonicalize explicit ids at capture ───────────────────────────

    [TestMethod]
    public async Task PlanSourcesAsync_UppercaseExplicitId_AddPath_ResolvedIdIsCanonicalLowercase()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string uppercaseId = "C8111111-1111-4111-8111-111111111177";

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(uppercaseId, title: "A Brand New Film (Canonical Id Test)")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(uppercaseId.ToLowerInvariant(), sourceAction.EntityId, "An uppercase file-authored explicit id must canonicalize to lowercase at capture");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_UppercaseExplicitId_CorrectionMatch_IndexedIdIsCanonicalLowercase()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string canonicalId = "c9111111-1111-4111-8111-111111111178";
        await SeedExplicitSourceAsync(conn, canonicalId, title: "Casablanca");
        SourceQuoteDto quote = BuildQuote("ca111111-1111-4111-8111-111111111179", source: "Casablanca (Corrected)");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(canonicalId.ToUpperInvariant(), title: "Casablanca (Corrected)")]);

        Assert.ContainsSingle(a => a.EntityType == "Source", actions, "The correction-match must be found via case-insensitive lookup — no duplicate Add");
        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        QuoteActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual(canonicalId, payload.SourceId, "sourceIndex must be seeded with the canonicalized (lowercase) form of the file's uppercase id, not the raw file casing, so a same-batch quote resolves to the row's real stored id");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_QuoteReferencesUppercaseExplicitSource_ResolvedSourceIdIsCanonical()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string uppercaseId = "CB111111-1111-4111-8111-111111111180";
        SourceQuoteDto quote = BuildQuote("cc111111-1111-4111-8111-111111111181", source: "A Brand New Film (Join Canonical Test)");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(uppercaseId, title: "A Brand New Film (Join Canonical Test)")]);

        ImportActionEntity sourceAction = actions.Single(a => a.EntityType == "Source");
        ImportActionEntity quoteAction  = actions.Single(a => a.EntityType == "Quote");
        QuoteActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual(sourceAction.EntityId, payload.SourceId, "The quote must resolve to the same canonical id the Source Add itself staged");
        Assert.AreEqual(uppercaseId.ToLowerInvariant(), payload.SourceId);
    }

    [TestMethod]
    public async Task PlanPeopleAsync_UppercaseExplicitId_ResolvedIdIsCanonicalLowercase()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string uppercaseId = "E4111111-1111-4111-8111-111111111174";

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(uppercaseId, name: "A Brand New Person (Canonical Id Test)")]);

        ImportActionEntity personAction = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(uppercaseId.ToLowerInvariant(), personAction.EntityId, "An uppercase file-authored explicit id must canonicalize to lowercase at capture");
    }

    [TestMethod]
    public async Task PlanStageDirectionsAsync_UppercaseExplicitId_ResolvedIdIsCanonicalLowercase()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string uppercaseId = "DC111111-1111-4111-8111-111111111177";

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            stageDirections: [BuildStageDirectionEntry(uppercaseId, text: "A brand new stage direction (canonical id test).")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "StageDirection");
        Assert.AreEqual(uppercaseId.ToLowerInvariant(), action.EntityId, "An uppercase file-authored explicit id must canonicalize to lowercase at capture");
    }

    [TestMethod]
    public async Task PlanSoundCuesAsync_UppercaseExplicitId_ResolvedIdIsCanonicalLowercase()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string uppercaseId = "DD111111-1111-4111-8111-111111111178";

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            soundCues: [BuildSoundCueEntry(uppercaseId, text: "A brand new sound cue (canonical id test).")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "SoundCue");
        Assert.AreEqual(uppercaseId.ToLowerInvariant(), action.EntityId, "An uppercase file-authored explicit id must canonicalize to lowercase at capture");
    }

    [TestMethod]
    public async Task PlanConversationsAsync_UppercaseExplicitId_ResolvedIdIsCanonicalLowercase()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string uppercaseId = "DE111111-1111-4111-8111-111111111179";

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            conversations: [BuildConversationEntry(uppercaseId, description: "A brand new conversation (canonical id test).")]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Conversation");
        Assert.AreEqual(uppercaseId.ToLowerInvariant(), action.EntityId, "An uppercase file-authored explicit id must canonicalize to lowercase at capture");
    }

    /// <summary>
    /// Quotes.Id canonicalizes to lowercase, matching every other entity's convention
    /// (EntityIdentity.StableId, GuidExtensions.ToCanonicalId) — this project's single settled id
    /// format after two prior revisions (ADR 012's revision history).
    /// </summary>
    [TestMethod]
    public async Task PlanAsync_UppercaseExplicitQuoteId_ResolvedIdIsCanonicalLowercase()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string uppercaseId = "DF111111-1111-4111-8111-111111111180";
        SourceQuoteDto quote = BuildQuote(uppercaseId, source: "A Brand New Film (Quote Canonical Id Test)");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        ImportActionEntity quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(uppercaseId.ToLowerInvariant(), quoteAction.EntityId, "An uppercase file-authored explicit quote id must canonicalize to lowercase at capture");
    }

    /// <summary>
    /// #209/#210: a conversation line's QuoteId reference must be canonicalized identically to how
    /// the quote it points at is canonicalized, or ConversationLines' real FOREIGN KEY constraint to
    /// Quotes(Id) fails once the referenced quote's own id no longer matches the file's raw casing —
    /// the exact bug class #209 found for StageDirectionId/SoundCueId, now also covering QuoteId.
    /// </summary>
    [TestMethod]
    public async Task PlanConversationsAsync_UppercaseQuoteIdInLine_CanonicalizedToLowercase()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string uppercaseQuoteId = "DF222222-2222-4222-8222-222222222280";
        SourceQuoteDto quote = BuildQuote(uppercaseQuoteId, source: "A Film With A Referenced Line (Canonical Id Test)");
        SourceConversationDto conversationEntry = new SourceConversationDto
        {
            Id          = "df333333-3333-4333-8333-333333333380",
            Description = "A conversation referencing an uppercase-authored quote id.",
            Lines       = [new SourceConversationLineDto { Order = 0, Type = Core.Enums.ConversationLineType.Quote, QuoteId = uppercaseQuoteId }],
        };

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            conversations: [conversationEntry]);

        ImportActionEntity conversationAction = actions.Single(a => a.EntityType == "Conversation");
        ConversationActionPayloadDto payload = System.Text.Json.JsonSerializer.Deserialize<ConversationActionPayloadDto>(conversationAction.IncomingValue!)!;
        Assert.AreEqual(uppercaseQuoteId.ToLowerInvariant(), payload.Lines[0].QuoteId,
            "A conversation line's QuoteId must be canonicalized to lowercase, matching the referenced quote's own canonical id — otherwise the ConversationLines FOREIGN KEY constraint to Quotes(Id) fails");
    }

    // ── #190: absent vs. explicit-null distinguishability ────────────────────

    [TestMethod]
    public async Task PlanSourcesAsync_ExplicitId_DateAbsent_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e0111111-1111-4111-8111-111111111181";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", date: "1942");

        SourceEntryDto entry = new SourceEntryDto { Id = id, Title = "Casablanca", Type = Core.Enums.QuoteType.Movie };
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [entry]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source" && a.ActionType.Parsed != ImportActionKind.Unchanged), "An omitted 'date' must never be treated as a change, under any policy");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_ExplicitId_DateExplicitlyNull_StagesModifyResettingDate()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e0111111-1111-4111-8111-111111111182";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", date: "1942");

        SourceEntryDto entry = new SourceEntryDto { Id = id, Title = "Casablanca", Type = Core.Enums.QuoteType.Movie, Date = Optional<string>.Of(null) };
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [entry]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed, "An explicit 'date: null' must resolve to a genuine reset");
        SourceActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(action.MergedFields!)!;
        Assert.IsNull(merged.Date);
    }

    [TestMethod]
    public async Task PlanSourcesAsync_ExplicitId_SeriesNameAbsent_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string seriesId = await SeedExistingSeriesAsync(conn, "The Hobbit");
        string id = "e0111111-1111-4111-8111-111111111183";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", date: "1942", seriesId: seriesId);

        SourceEntryDto entry = new SourceEntryDto { Id = id, Title = "Casablanca", Type = Core.Enums.QuoteType.Movie, Date = "1942" };
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [entry]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source" && a.ActionType.Parsed != ImportActionKind.Unchanged), "An omitted 'seriesName' must never be treated as a change, under any policy — same bug as Date, found on the same DTO one field over (#190 scope-expansion finding)");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_ExplicitId_SeriesNameExplicitlyNull_StagesModifyClearingSeries()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string seriesId = await SeedExistingSeriesAsync(conn, "The Hobbit");
        string id = "e0111111-1111-4111-8111-111111111184";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", date: "1942", seriesId: seriesId);

        SourceEntryDto entry = new SourceEntryDto { Id = id, Title = "Casablanca", Type = Core.Enums.QuoteType.Movie, Date = "1942", SeriesName = Optional<string>.Of(null) };
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [entry]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed, "An explicit 'seriesName: null' must resolve to a genuine clear");
        SourceActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(action.MergedFields!)!;
        Assert.IsNull(merged.SeriesId);
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NaturalKey_DateExplicitlySet_NowTakesEffect()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        // Not referenced by the entry below — a row found only by natural key (Title+Type).
        await SeedExplicitSourceAsync(conn, "e0111111-1111-4111-8111-111111111185", title: "Casablanca", date: null);

        // #180's enrichment shape: no explicit id.
        SourceEntryDto entry = new SourceEntryDto { Title = "Casablanca", Type = Core.Enums.QuoteType.Movie, Date = "1975" };
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [entry]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed,
            "#190 requirement 6's liberalization: a natural-key entry that explicitly sets 'date' now actually takes effect, where it was previously always silently ignored regardless of what the file said");
        SourceActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(action.MergedFields!)!;
        Assert.AreEqual("1975", merged.Date);
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NaturalKey_MergeOurs_ExistingSeriesWins()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string originalSeriesId = await SeedExistingSeriesAsync(conn, "Original Series");
        await SeedExistingSeriesAsync(conn, "New Series");
        // Not referenced by the entry below — a row found only by natural key (Title+Type).
        await SeedExplicitSourceAsync(conn, "e0111111-1111-4111-8111-111111111186", title: "Casablanca", seriesId: originalSeriesId);

        SourceEntryDto entry = new SourceEntryDto { Title = "Casablanca", Type = Core.Enums.QuoteType.Movie, SeriesName = "New Series" };
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.MergeOurs,
            sources: [entry]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Source");
        SourceActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(action.MergedFields!)!;
        Assert.AreEqual(originalSeriesId, merged.SeriesId,
            "#190 drive-by fix: MergeOurs must keep the existing Series on a genuine conflict — this branch previously never consulted FieldMergeResolver at all and always took the incoming value unconditionally");
    }

    [TestMethod]
    public async Task PlanPeopleAsync_DateOfBirthAbsent_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e0111111-1111-4111-8111-111111111187";
        await SeedExplicitPersonAsync(conn, id, name: "Ada Lovelace", dateOfBirth: "1815-12-10", dateOfDeath: "1852-11-27");

        PersonEntryDto entry = new PersonEntryDto { Id = id, Name = "Ada Lovelace", DateOfDeath = "1852-11-27" };
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [entry]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Person" && a.ActionType.Parsed != ImportActionKind.Unchanged), "An omitted 'dateOfBirth' must never be treated as a change, under any policy");
    }

    [TestMethod]
    public async Task PlanPeopleAsync_DateOfDeathExplicitlyNull_StagesModifyResettingDate()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e0111111-1111-4111-8111-111111111188";
        await SeedExplicitPersonAsync(conn, id, name: "Ada Lovelace", dateOfBirth: "1815-12-10", dateOfDeath: "1852-11-27");

        PersonEntryDto entry = new PersonEntryDto { Id = id, Name = "Ada Lovelace", DateOfBirth = "1815-12-10", DateOfDeath = Optional<string>.Of(null) };
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [entry]);

        ImportActionEntity action = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed, "An explicit 'dateOfDeath: null' must resolve to a genuine reset");
        PersonActionPayloadDto merged = System.Text.Json.JsonSerializer.Deserialize<PersonActionPayloadDto>(action.MergedFields!)!;
        Assert.IsNull(merged.DateOfDeath);
    }

    [TestMethod]
    public async Task PlanStageDirectionsAsync_ImageUrlAbsent_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e0111111-1111-4111-8111-111111111189";
        await SeedExplicitStageDirectionAsync(conn, id, text: "A shot rings out.", imageUrl: "http://example.com/still.jpg");

        SourceStageDirectionDto entry = new SourceStageDirectionDto { Id = id, Text = "A shot rings out." };
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            stageDirections: [entry]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "StageDirection" && a.ActionType.Parsed != ImportActionKind.Unchanged), "An omitted 'imageUrl' must never be treated as a change, under any policy — must preserve a real existing value, not just null-matches-null");
    }

    [TestMethod]
    public async Task PlanSoundCuesAsync_SoundFileUrlAbsent_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e0111111-1111-4111-8111-111111111191";
        await SeedExplicitSoundCueAsync(conn, id, text: "Distant thunder.", soundFileUrl: "http://example.com/thunder.mp3", imageUrl: "http://example.com/img.jpg");

        SourceSoundCueDto entry = new SourceSoundCueDto { Id = id, Text = "Distant thunder.", ImageUrl = "http://example.com/img.jpg" };
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            soundCues: [entry]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "SoundCue" && a.ActionType.Parsed != ImportActionKind.Unchanged), "An omitted 'soundFileUrl' must never be treated as a change, under any policy — must preserve a real existing value, not just null-matches-null");
    }

    [TestMethod]
    public async Task PlanConversationsAsync_DescriptionAbsent_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string id = "e0111111-1111-4111-8111-111111111192";
        await SeedExplicitConversationAsync(conn, id, description: "A tense standoff.");

        SourceConversationDto entry = new SourceConversationDto { Id = id, Lines = [] };
        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            conversations: [entry]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Conversation" && a.ActionType.Parsed != ImportActionKind.Unchanged), "An omitted 'description' must never be treated as a change, under any policy");
    }
}
