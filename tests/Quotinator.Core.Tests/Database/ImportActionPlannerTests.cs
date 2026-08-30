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
        var importBatches = new SqliteImportBatchRepository(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        var actionReader  = new ImportActionReader(_factory);
        var actionWriter  = new ImportActionWriter(_factory);
        var coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, _factory);
        var actionService = new SqliteImportActionService(actionReader, coordinator, actionWriter, NoOpAuditEntryWriter.Instance, NoOpChangeWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SourceEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<CharacterEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<PersonEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            importBatches, _factory, NoOpNotificationWriter.Instance);
        var db = new QuotinatorDatabaseInitializer(_factory, options, QuotinatorMigrations.All, [], importBatches,
            coordinator, actionService, actionWriter, NoOpAuditEntryWriter.Instance,
            NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance, NoOpSourceCacheUpdater.Instance,
            autoUpdateSources: false,
            autoPurgeBundledImportActions: false, autoPurgeUserImportActions: false,
            NoOpRuleFileOverridePathResolver.Instance, NoOpSourceFileOverrideRegistry.Instance, NoOpFileResourceRepository.Instance,
            NoOpNotificationReader.Instance, NoOpNotificationWriter.Instance, NoOpNotificationTextSource.Instance,
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

        Assert.HasCount(4, actions, "Quote + Source + Character + Person, all brand new");
        Assert.IsTrue(actions.All(a => a.ActionType.Parsed == ImportActionKind.Add));
        Assert.IsTrue(actions.All(a => a.Status.Parsed == ImportActionStatus.Decided), "Add is never ambiguous");

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(quote.Id, quoteAction.EntityId);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(EntityIdentity.SourceId("Casablanca", "Movie"), sourceAction.EntityId);

        var characterAction = actions.Single(a => a.EntityType == "Character");
        // #174/ADR 013: CharacterId's stable-id derivation is (sourceId, name, sourceType).
        Assert.AreEqual(EntityIdentity.CharacterId(sourceAction.EntityId, "Rick Blaine", "Movie"), characterAction.EntityId);

        var personAction = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(EntityIdentity.PersonId("Someone"), personAction.EntityId);
    }

    [TestMethod]
    public async Task PlanAsync_NoCharacterOrAuthor_StagesOnlyQuoteAndSourceActions()
    {
        using var conn = await OpenConnectionAsync();
        var quote = BuildQuote("21111111-1111-4111-8111-111111111111", character: null);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.HasCount(2, actions);
        Assert.AreSequenceEqual(["Quote", "Source"], [.. actions.Select(a => a.EntityType)], Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task PlanAsync_ExistingSourceCharacterPerson_ReusesRealIds_NoAddActionsForThem()
    {
        using var conn = await OpenConnectionAsync();

        var realSourceId    = Guid.NewGuid();
        var realCharacterId = Guid.NewGuid();
        var realPersonId    = Guid.NewGuid();
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

        var quote = BuildQuote("31111111-1111-4111-8111-111111111111", author: "Someone");
        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.HasCount(1, actions, "Only the Quote is new — Source/Character/Person all already exist");
        var quoteAction = actions.Single();
        Assert.AreEqual("Quote", quoteAction.EntityType);

        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        // GuidExtensions.ToCanonicalId (and GuidHandler, given RemoveTypeMap) render every Guid column
        // as lowercase "D"-format TEXT (ADR 012) — the resolved id must match that convention.
        Assert.AreEqual(realSourceId.ToString("D"), payload.SourceId, "Must resolve to the real existing Source id, not a stable id");
        Assert.AreEqual(realCharacterId.ToString("D"), payload.CharacterId);
        Assert.AreEqual(realPersonId.ToString("D"), payload.PersonId);
    }

    // ── #174/ADR 013: Character global identity, Series-scoped cross-Source resolution ──────────

    private static async Task<string> SeedGlobalCharacterAsync(SqliteConnection conn, string name, string sourceId, string sourceType)
    {
        var characterId = Guid.NewGuid();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("INSERT INTO Quotinator_Character (Id, Name, SourceType, DateCreated) VALUES (@Id, @Name, @SourceType, @now)",
            new { Id = characterId, Name = name, SourceType = sourceType, now });
        await conn.ExecuteAsync("INSERT INTO Quotinator_CharacterSource (Id, CharacterId, SourceId, DateCreated) VALUES (@Id, @CharacterId, @SourceId, @now)",
            new { Id = Guid.NewGuid(), CharacterId = characterId, SourceId = sourceId, now });
        return characterId.ToString("D");
    }

    private static async Task<string> SeedSourceAsync(SqliteConnection conn, string title, string type = "Movie", string? seriesId = null)
    {
        var sourceId = Guid.NewGuid();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("INSERT INTO Quotinator_Source (Id, Title, Type, SeriesId, DateCreated) VALUES (@Id, @Title, @Type, @SeriesId, @now)",
            new { Id = sourceId, Title = title, Type = type, SeriesId = seriesId, now });
        return sourceId.ToString("D");
    }

    [TestMethod]
    public async Task ResolveCharacterAsync_ExistingGlobalCharacter_ReusesRealId()
    {
        using var conn = await OpenConnectionAsync();
        var existingSourceId = await SeedSourceAsync(conn, "Existing Film");
        var existingCharacterId = await SeedGlobalCharacterAsync(conn, "Gandalf", existingSourceId, "Movie");

        var quote = BuildQuote("e1111111-1111-4111-8111-111111111111", source: "Existing Film", character: "Gandalf");
        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Character"), "Already linked to this exact Source — silently reused, no Add staged");
        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual(existingCharacterId, payload.CharacterId);
    }

    /// <summary>ADR 013 Decision 7: a same-Name, same-Type Character already linked to a DIFFERENT Source that shares this quote's Source's Series must be reused, not duplicated.</summary>
    [TestMethod]
    public async Task ResolveCharacterAsync_SeriesScopedCrossSourceMatch_ReusesExistingCharacter()
    {
        using var conn = await OpenConnectionAsync();
        var seriesId = await SeedExistingSeriesAsync(conn, "The Lord of the Rings");
        var film1Id = await SeedSourceAsync(conn, "The Fellowship of the Ring", seriesId: seriesId);
        var existingCharacterId = await SeedGlobalCharacterAsync(conn, "Gandalf", film1Id, "Movie");
        await SeedSourceAsync(conn, "The Two Towers", seriesId: seriesId); // not directly referenced — proves the match isn't keyed off a specific pre-known Source row

        var quote = BuildQuote("e2111111-1111-4111-8111-111111111111", source: "The Two Towers", character: "Gandalf");
        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Character"), "A Series-scoped cross-Source match is reused directly, like the same-Source case");
        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual(existingCharacterId, payload.CharacterId, "Must resolve to the existing global Character, not stage a duplicate");
    }

    [TestMethod]
    public async Task ResolveCharacterAsync_DifferingSourceType_NeverReusesExistingCharacter()
    {
        using var conn = await OpenConnectionAsync();
        var seriesId = await SeedExistingSeriesAsync(conn, "Middle Earth Adaptations");
        var movieSourceId = await SeedSourceAsync(conn, "The Fellowship of the Ring (Film)", type: "Movie", seriesId: seriesId);
        await SeedGlobalCharacterAsync(conn, "Gandalf", movieSourceId, "Movie");
        await SeedSourceAsync(conn, "The Fellowship of the Ring (Book)", type: "Book", seriesId: seriesId);

        var quote = BuildQuote("e3111111-1111-4111-8111-111111111111", source: "The Fellowship of the Ring (Book)", character: "Gandalf", type: Core.Enums.QuoteType.Book);
        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.ContainsSingle(a => a.EntityType == "Character", actions, "Source.Type anchor (ADR 011) must never be crossed, even within a shared Series");
    }

    [TestMethod]
    public async Task ResolveCharacterAsync_NoKnownSeriesRelationship_CreatesSeparateCharacter()
    {
        using var conn = await OpenConnectionAsync();
        var unrelatedSourceId = await SeedSourceAsync(conn, "An Unrelated Movie", seriesId: null);
        await SeedGlobalCharacterAsync(conn, "Sam", unrelatedSourceId, "Movie");
        await SeedSourceAsync(conn, "A Different Unrelated Movie", seriesId: null);

        var quote = BuildQuote("e4111111-1111-4111-8111-111111111111", source: "A Different Unrelated Movie", character: "Sam");
        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.ContainsSingle(a => a.EntityType == "Character", actions, "Same Name, same Type, but no known Series relationship — conservative default must create a new, separate Character");
    }

    [TestMethod]
    public async Task ResolveCharacterAsync_ExistingGlobalCharacter_CaseInsensitiveNameMatch_ReusesRealId()
    {
        using var conn = await OpenConnectionAsync();
        var existingSourceId = await SeedSourceAsync(conn, "Existing Film");
        var existingCharacterId = await SeedGlobalCharacterAsync(conn, "Gandalf", existingSourceId, "Movie");

        var quote = BuildQuote("e5111111-1111-4111-8111-111111111111", source: "Existing Film", character: "GANDALF");
        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Character"), "Name matching is case-insensitive — storage keeps original casing, comparison does not");
        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual(existingCharacterId, payload.CharacterId);
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
        using var conn = await OpenConnectionAsync();
        var id = "c1111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id);

        var quote = BuildQuote(id, source: "Casablanca", quoteText: "A changed line.");
        var rules = new ConflictRuleLookup([BuildQuoteTextKeepRule(id)]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed, "A matching rule for the only changed field must auto-resolve instead of leaving it Pending");
        Assert.IsNotNull(quoteAction.MergedFields, "An auto-resolved action already has its final values computed, the same as any other Decided action");
        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.MergedFields!)!;
        Assert.AreEqual("Original text", payload.Fields.QuoteText, "Keep must resolve to the existing side's value");
    }

    [TestMethod]
    public async Task PlanAsync_ReviewPolicy_RuleCoversOnlySomeChangedFields_StillStagesPending()
    {
        using var conn = await OpenConnectionAsync();
        var id = "c2111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteWithCharacterAsync(conn, id, quoteText: "Original text", characterName: "Rick Blaine");

        var quote = BuildQuote(id, source: "Casablanca", quoteText: "A changed line.", character: "Ilsa Lund");
        var rules = new ConflictRuleLookup([BuildQuoteTextKeepRule(id)]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Pending, quoteAction.Status.Parsed, "The character field is also ambiguous and has no matching rule — a partial rule match must not auto-resolve the whole action");
        Assert.IsNull(quoteAction.MergedFields, "Pending actions have no resolved values yet");
    }

    [TestMethod]
    public async Task PlanAsync_ReviewPolicy_NonMatchingRuleLookup_StagesPendingAsToday()
    {
        using var conn = await OpenConnectionAsync();
        var id = "c3111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id);

        var quote = BuildQuote(id, source: "Casablanca", quoteText: "A changed line.");
        var rules = new ConflictRuleLookup([BuildQuoteTextKeepRule("00000000-0000-4000-8000-000000000000")]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Pending, quoteAction.Status.Parsed, "A rule for a different quote id must not affect this one — regression guard matching pre-#181 behaviour");
    }

    [TestMethod]
    public async Task PlanAsync_ReviewPolicy_MatchingRuleButCompletenessGuardBlocks_StillStagesBlockedNotDecided()
    {
        using var conn = await OpenConnectionAsync();
        var id = "c4111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(conn, id, completenessStatus: "Complete");

        var quote = BuildQuote(id, source: "Casablanca", quoteText: "A changed line.");
        var rules = new ConflictRuleLookup([BuildQuoteTextKeepRule(id)]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
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
        using var conn = await OpenConnectionAsync();
        var id = "d1111111-1111-4111-8111-111111111111";
        var quote = BuildQuote(id, source: "Airplane!", character: null);
        var rules = new ConflictRuleLookup([BuildCharacterCustomRule(id, "Steve McCroskey")]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionKind.Add, quoteAction.ActionType.Parsed, "This is still a genuine first-ever encounter, not a Modify");
        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual("Steve McCroskey", payload.Fields.Character, "A matching Custom rule must correct the field on a brand-new Add, not only on a later Modify");
    }

    [TestMethod]
    public async Task PlanAsync_BrandNewQuote_MatchingCustomRule_CharacterResolvesAgainstCorrectedValue()
    {
        using var conn = await OpenConnectionAsync();
        var id = "d2111111-1111-4111-8111-111111111111";
        var quote = BuildQuote(id, source: "Airplane!", character: null);
        var rules = new ConflictRuleLookup([BuildCharacterCustomRule(id, "Steve McCroskey")]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        // Without the fix, Character resolution runs against the raw (null) value and no Character
        // action is staged at all — the corrected text would show on the Quote but never link to a
        // real Character entity.
        var characterAction = actions.SingleOrDefault(a => a.EntityType == "Character");
        Assert.IsNotNull(characterAction, "The corrected character value must also drive Character entity resolution, not just the Quote's own display field");
        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(EntityIdentity.CharacterId(sourceAction.EntityId, "Steve McCroskey", "Movie"), characterAction!.EntityId);
    }

    [TestMethod]
    public async Task PlanAsync_BrandNewQuote_StaleCustomRule_DoesNotApply()
    {
        using var conn = await OpenConnectionAsync();
        var id = "d3111111-1111-4111-8111-111111111111";
        // The rule was authored assuming "character" comes in as null — this quote's raw incoming
        // character is no longer null (the upstream data changed since the rule was written), so the
        // rule's own recorded snapshot for this exact field no longer matches and it must not apply.
        var staleRule = new ConflictResolutionRule
        {
            EntityId = id,
            ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"character":null}"""),
            IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"character":null}"""),
            Fields = [new ConflictResolutionFieldRule { Field = "character", Resolution = FieldResolutionChoice.Custom, CustomValue = "Steve McCroskey" }],
        };
        var quote = BuildQuote(id, source: "Airplane!", character: "Some Newly-Added Value");
        var rules = new ConflictRuleLookup([staleRule]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual("Some Newly-Added Value", payload.Fields.Character, "A stale rule (recorded snapshot no longer matches this field's real value) must never silently apply, on Add or Modify");
    }

    [TestMethod]
    public async Task PlanAsync_BrandNewQuote_KeepOrReplaceRuleField_IsNoOpOnAdd()
    {
        using var conn = await OpenConnectionAsync();
        var id = "d4111111-1111-4111-8111-111111111111";
        var quote = BuildQuote(id, source: "Casablanca", quoteText: "Here's looking at you, kid.");
        // A Keep/Replace rule has no second side to choose between on a brand-new Add — must be a no-op.
        var rules = new ConflictRuleLookup([BuildQuoteTextKeepRule(id)]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.Review, conflictRules: rules);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionKind.Add, quoteAction.ActionType.Parsed);
        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual("Here's looking at you, kid.", payload.Fields.QuoteText, "Keep/Replace on a first-ever Add must be a no-op, not an error");
    }

    // ── #181: source-title alias lookup ────────────────────────────────────────

    private static async Task SeedExistingQuoteWithSourceAsync(SqliteConnection conn, string quoteId, string sourceId, string sourceTitle, string sourceType, string quoteText)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("INSERT INTO Quotinator_Source (Id, Title, Type, DateCreated) VALUES (@Id, @sourceTitle, @sourceType, @now)",
            new { Id = sourceId, sourceTitle, sourceType, now });
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Quote (Id, QuoteText, OriginalLanguage, SourceId, DateCreated) VALUES (@Id, @quoteText, 'en', @SourceId, @now)",
            new { Id = quoteId, quoteText, SourceId = sourceId, now });
    }

    [TestMethod]
    public async Task PlanAsync_SourceAliasMatches_ResolvesToExistingCanonicalSource_NoSpuriousSourceAdd()
    {
        using var conn = await OpenConnectionAsync();
        var canonicalSourceId = Guid.NewGuid().ToString();
        await SeedExplicitSourceAsync(conn, canonicalSourceId, title: "The Avengers", type: "Movie", date: null);

        var quote   = BuildQuote("d1111111-1111-4111-8111-111111111111", source: "Marvel's The Avengers", character: null);
        var aliases = new SourceAliasLookup([
            new SourceAliasRule { Title = "Marvel's The Avengers", Type = "movie", CanonicalTitle = "The Avengers", CanonicalType = "movie" },
        ]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        Assert.DoesNotContain(a => a.EntityType == "Source", actions, "The alias must resolve to the already-existing canonical Source — no new SourceEntity Add should be staged");
        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        var payload     = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
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
        using var conn = await OpenConnectionAsync();
        var quoteId  = "e1111111-1111-4111-8111-111111111111";
        var sourceId = Guid.NewGuid().ToString();
        await SeedExistingQuoteWithSourceAsync(conn, quoteId, sourceId, "Zootopia", "Movie", "Original text.");

        var quote   = BuildQuote(quoteId, source: "Zootopia", quoteText: "Original text.", type: Core.Enums.QuoteType.Anime);
        var aliases = new SourceAliasLookup([
            new SourceAliasRule { Title = "Zootopia", Type = "anime", CanonicalTitle = "Zootopia", CanonicalType = "movie" },
        ]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        Assert.DoesNotContain(a => a.EntityType == "Source", actions, "The alias must normalise type before Source resolution runs — no spurious anime-typed Source should ever be staged");
        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed);
        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.MergedFields!)!;
        Assert.AreEqual(sourceId, payload.SourceId, "Must resolve to the original existing Source id, not a new alias-derived one");
    }

    [TestMethod]
    public async Task PlanAsync_NoSourceAliasesProvided_RawTitleUsedAsBefore()
    {
        using var conn = await OpenConnectionAsync();
        var quote = BuildQuote("d2111111-1111-4111-8111-111111111111", source: "Marvel's The Avengers", character: null);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        var payload      = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.IncomingValue!)!;
        Assert.AreEqual("Marvel's The Avengers", payload.Title, "With no alias lookup provided, the raw incoming title is used unchanged — regression guard matching pre-#181 behaviour");
    }

    // ── #153: SourceAliasRule staleness ──────────────────────────────────────────

    /// <summary>
    /// Simulates a genuine rename: the Source that was originally created under exactly the alias's
    /// own recorded canonical (title, type) — and therefore carries the id that pair deterministically
    /// hashes to (<see cref="EntityIdentity.SourceId"/>, fixed at creation, never recomputed on a later
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
        using var conn = await OpenConnectionAsync();
        var renamedSourceId = EntityIdentity.SourceId("The Avengers", "movie");
        await SeedExplicitSourceAsync(conn, renamedSourceId, title: "The Avengers (Renamed)", type: "Movie", date: null);

        var quote   = BuildQuote("f1111111-1111-4111-8111-111111111111", source: "Marvel's The Avengers", character: null);
        var aliases = new SourceAliasLookup([
            new SourceAliasRule { Title = "Marvel's The Avengers", Type = "movie", CanonicalTitle = "The Avengers", CanonicalType = "movie" },
        ]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Stale, quoteAction.Status.Parsed, "The row the alias's canonical pair would hash to now has a different live Title — a genuine rename, must never be silently trusted");
        Assert.IsNull(quoteAction.MergedFields, "A Stale action has nothing resolved yet, same as Pending/Blocked");
    }

    [TestMethod]
    public async Task PlanAsync_SourceAliasStale_ModifyPath_StagesQuoteAsStaleNotDecided()
    {
        using var conn = await OpenConnectionAsync();
        var quoteId  = "f2111111-1111-4111-8111-111111111111";
        var sourceId = Guid.NewGuid().ToString();
        await SeedExistingQuoteWithSourceAsync(conn, quoteId, sourceId, "Zootopia", "Movie", "Original text.");
        var renamedCanonicalId = EntityIdentity.SourceId("Zootopia (Canonical)", "movie");
        await SeedExplicitSourceAsync(conn, renamedCanonicalId, title: "Zootopia (Canonical, Renamed)", type: "Movie", date: null);

        var quote   = BuildQuote(quoteId, source: "Zootopia", quoteText: "A changed line.", type: Core.Enums.QuoteType.Anime);
        var aliases = new SourceAliasLookup([
            new SourceAliasRule { Title = "Zootopia", Type = "anime", CanonicalTitle = "Zootopia (Canonical)", CanonicalType = "movie" },
        ]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
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
        using var conn = await OpenConnectionAsync();
        var quote   = BuildQuote("f4111111-1111-4111-8111-111111111111", source: "Marvel's The Avengers", character: null);
        var aliases = new SourceAliasLookup([
            new SourceAliasRule { Title = "Marvel's The Avengers", Type = "movie", CanonicalTitle = "The Avengers", CanonicalType = "movie" },
        ]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed, "No Source has ever existed under this canonical name yet — this is a legitimate first-time creation, not staleness");
        var sourceAction = actions.Single(a => a.EntityType == "Source");
        var payload      = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.IncomingValue!)!;
        Assert.AreEqual("The Avengers", payload.Title, "The new SourceEntity must be created under the alias's canonical title, not the raw incoming one");
    }

    [TestMethod]
    public async Task PlanAsync_SourceAliasFresh_CanonicalSourceExists_RegressionStillDecided()
    {
        using var conn = await OpenConnectionAsync();
        var canonicalSourceId = Guid.NewGuid().ToString();
        await SeedExplicitSourceAsync(conn, canonicalSourceId, title: "The Avengers", type: "Movie", date: null);

        var quote   = BuildQuote("f3111111-1111-4111-8111-111111111111", source: "Marvel's The Avengers", character: null);
        var aliases = new SourceAliasLookup([
            new SourceAliasRule { Title = "Marvel's The Avengers", Type = "movie", CanonicalTitle = "The Avengers", CanonicalType = "movie" },
        ]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, sourceAliases: aliases);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        Assert.AreEqual(ImportActionStatus.Decided, quoteAction.Status.Parsed, "A fresh alias (canonical Source still exists) must resolve normally, not be treated as stale");
    }

    private static async Task SeedExistingQuoteWithCharacterAsync(SqliteConnection conn, string id, string quoteText, string characterName)
    {
        var now          = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var sourceId     = Guid.NewGuid();
        var characterId  = Guid.NewGuid();
        var characterSourceId = Guid.NewGuid();
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
        using var conn = await OpenConnectionAsync();
        var q1 = BuildQuote("61111111-1111-4111-8111-111111111111", character: "Rick Blaine");
        var q2 = BuildQuote("71111111-1111-4111-8111-111111111111", character: "Ilsa Lund");

        var actions = await ImportActionPlanner.PlanAsync(conn, [q1, q2], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.ContainsSingle(a => a.EntityType == "Source", actions, "Both quotes share the same Source — must be staged once, not twice");
    }

    [TestMethod]
    public async Task ResolveSourceAsync_QuoteWithDate_StagesSourceAddCarryingThatDate()
    {
        using var conn = await OpenConnectionAsync();
        var quote = BuildQuote("61211111-1111-4111-8111-111111111111", date: "1993");

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        var payload = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.IncomingValue!)!;
        Assert.AreEqual("1993", payload.Date, "The resolving quote's own Date must carry through to the staged Source Add payload");
    }

    [TestMethod]
    public async Task ResolveSourceAsync_TwoQuotesSameSourceDifferentDates_FirstQuotesDateWins()
    {
        using var conn = await OpenConnectionAsync();
        var q1 = BuildQuote("61311111-1111-4111-8111-111111111111", character: "Rick Blaine", date: "1942");
        var q2 = BuildQuote("61411111-1111-4111-8111-111111111111", character: "Ilsa Lund", date: "1943");

        var actions = await ImportActionPlanner.PlanAsync(conn, [q1, q2], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        var payload = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.IncomingValue!)!;
        Assert.AreEqual("1942", payload.Date, "Only one Source Add is staged for both quotes — the first-encountered quote's Date wins, matching the existing Title/Type first-quote-wins behaviour");
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

    [TestMethod]
    public async Task ResolveSourceAsync_ExistingDatedSource_QuoteWithDifferentDate_NoActionStaged()
    {
        using SqliteConnection conn = await OpenConnectionAsync();
        string sourceId = Guid.NewGuid().ToString("D");
        await SeedExplicitSourceAsync(conn, sourceId, title: "Casablanca", type: "Movie", date: "1942", completenessStatus: "Incomplete");
        SourceQuoteDto quote = BuildQuote("c2111111-1111-4111-8111-111111111111", date: "1999");

        IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.DoesNotContain(a => a.EntityType == "Source", actions, "An already-dated Source must never be touched by a later, differently-dated quote — first-found-wins, no invented conflict logic");
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
        using var conn = await OpenConnectionAsync();
        var quote = BuildQuote("81111111-1111-4111-8111-111111111111", author: "Someone");

        await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Quote"));
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Source"));
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Character"));
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Person"));
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
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
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
        var id  = Guid.NewGuid().ToString("D");
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Series (Id, Name, UniverseId, CompletenessStatus, DateCreated) VALUES (@Id, @Name, @UniverseId, 'Incomplete', @now)",
            new { Id = id, Name = name, UniverseId = universeId, now });
        return id;
    }

    private static async Task<string> SeedExistingUniverseAsync(SqliteConnection conn, string name = "Middle Earth")
    {
        var id  = Guid.NewGuid().ToString("D");
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Universe (Id, Name, CompletenessStatus, DateCreated) VALUES (@Id, @Name, 'Incomplete', @now)",
            new { Id = id, Name = name, now });
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
            "INSERT INTO Quotinator_Universe (Id, Name, CompletenessStatus, DateCreated) VALUES (@Id, 'Middle Earth', 'Incomplete', @now)",
            new { Id = Guid.NewGuid().ToString("D").ToUpperInvariant(), now });

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            universe: [BuildUniverseEntry("Middle Earth")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Universe"), "Already exists by name — silently reused, no action staged");
    }

    /// <summary>
    /// #216 fix: Sql.Universe.SelectIdByName is now case-insensitive, matching #180's own
    /// Sql.Sources.SelectIdByTitleAndType precedent — a case-only difference must never stage a
    /// duplicate Universe.
    /// </summary>
    [TestMethod]
    public async Task PlanUniverseAsync_ExistingByName_DifferingCasing_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        await SeedExistingUniverseAsync(conn, "Middle Earth");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            universe: [BuildUniverseEntry("MIDDLE EARTH")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Universe"), "Differing casing must still match the existing row by natural key, not stage a duplicate Add");
    }

    /// <summary>#163: Universe's own two-shape widening — explicit id present, matched by that id, name differs.</summary>
    [TestMethod]
    public async Task PlanUniverseAsync_ExplicitIdMatchFound_NameDiffers_StagesModifyAction()
    {
        using var conn = await OpenConnectionAsync();
        var id = await SeedExistingUniverseAsync(conn, "Middle Earth");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            universe: [new UniverseEntryDto { Id = id, Name = "Middle-earth (corrected)" }]);

        var universeAction = actions.Single(a => a.EntityType == "Universe");
        Assert.AreEqual(ImportActionKind.Modify, universeAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, universeAction.Status.Parsed);
        var merged = System.Text.Json.JsonSerializer.Deserialize<UniverseActionPayloadDto>(universeAction.MergedFields!)!;
        Assert.AreEqual("Middle-earth (corrected)", merged.Name);
    }

    [TestMethod]
    public async Task PlanUniverseAsync_ReviewPolicy_MatchingRule_StagesDecidedNotPending()
    {
        using var conn = await OpenConnectionAsync();
        var id = await SeedExistingUniverseAsync(conn, "Middle Earth");
        var rules = new ConflictRuleLookup([
            new ConflictResolutionRule
            {
                EntityId = id,
                ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"Middle Earth"}"""),
                IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"Middle-earth (corrected)"}"""),
                Fields = [new ConflictResolutionFieldRule { Field = "name", Resolution = FieldResolutionChoice.Keep }],
            },
        ]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            universe: [new UniverseEntryDto { Id = id, Name = "Middle-earth (corrected)" }], conflictRules: rules);

        var universeAction = actions.Single(a => a.EntityType == "Universe");
        Assert.AreEqual(ImportActionStatus.Decided, universeAction.Status.Parsed, "A matching rule must auto-resolve instead of leaving it Pending");
        var merged = System.Text.Json.JsonSerializer.Deserialize<UniverseActionPayloadDto>(universeAction.MergedFields!)!;
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
        using var conn = await OpenConnectionAsync();
        var id = await SeedExistingUniverseAsync(conn, "Middle Earth");
        var rules = new ConflictRuleLookup([
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
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            universe: [new UniverseEntryDto { Id = id, Name = "Middle Earth" }], conflictRules: rules);

        var universeAction = actions.SingleOrDefault(a => a.EntityType == "Universe");
        Assert.IsNotNull(universeAction, "The Custom rule must produce an action even though nothing 'changed' in the ordinary sense");
        Assert.AreEqual(ImportActionStatus.Decided, universeAction!.Status.Parsed);
        var merged = System.Text.Json.JsonSerializer.Deserialize<UniverseActionPayloadDto>(universeAction.MergedFields!)!;
        Assert.AreEqual("Middle-earth", merged.Name, "Custom must resolve to customValue, not either side's actual value");
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

    /// <summary>
    /// #216 fix: Sql.Series.SelectIdByName is now case-insensitive, matching #180's own
    /// Sql.Sources.SelectIdByTitleAndType precedent — a case-only difference must never stage a
    /// duplicate Series.
    /// </summary>
    [TestMethod]
    public async Task PlanSeriesAsync_ExistingByName_DifferingCasing_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        await SeedExistingSeriesAsync(conn, "The Lord of the Rings");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            series: [BuildSeriesEntry("THE LORD OF THE RINGS")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Series"), "Differing casing must still match the existing row by natural key, not stage a duplicate Add");
    }

    /// <summary>#163: Series' own two-shape widening — explicit id present, matched by that id, name differs.</summary>
    [TestMethod]
    public async Task PlanSeriesAsync_ExplicitIdMatchFound_NameDiffers_StagesModifyAction()
    {
        using var conn = await OpenConnectionAsync();
        var id = await SeedExistingSeriesAsync(conn, "The Hobbit");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            series: [new SeriesEntryDto { Id = id, Name = "The Hobbit Trilogy" }]);

        var seriesAction = actions.Single(a => a.EntityType == "Series");
        Assert.AreEqual(ImportActionKind.Modify, seriesAction.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, seriesAction.Status.Parsed);
        var merged = System.Text.Json.JsonSerializer.Deserialize<SeriesActionPayloadDto>(seriesAction.MergedFields!)!;
        Assert.AreEqual("The Hobbit Trilogy", merged.Name);
    }

    /// <summary>#163: Series' own two-shape widening — explicit id present, matched by that id, universeId differs.</summary>
    [TestMethod]
    public async Task PlanSeriesAsync_ExplicitIdMatchFound_UniverseIdDiffers_StagesModifyAction()
    {
        using var conn = await OpenConnectionAsync();
        var originalUniverseId = await SeedExistingUniverseAsync(conn, "Middle Earth");
        var newUniverseId      = await SeedExistingUniverseAsync(conn, "The Shire Cinematic Universe");
        var id = await SeedExistingSeriesAsync(conn, "The Hobbit", universeId: originalUniverseId);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            series: [new SeriesEntryDto { Id = id, Name = "The Hobbit", UniverseName = "The Shire Cinematic Universe" }]);

        var seriesAction = actions.Single(a => a.EntityType == "Series");
        Assert.AreEqual(ImportActionKind.Modify, seriesAction.ActionType.Parsed);
        var merged = System.Text.Json.JsonSerializer.Deserialize<SeriesActionPayloadDto>(seriesAction.MergedFields!)!;
        Assert.AreEqual(newUniverseId.ToUpperInvariant(), merged.UniverseId?.ToUpperInvariant());
    }

    [TestMethod]
    public async Task PlanSeriesAsync_ReviewPolicy_MatchingRule_StagesDecidedNotPending()
    {
        using var conn = await OpenConnectionAsync();
        var id = await SeedExistingSeriesAsync(conn, "The Hobbit");
        var rules = new ConflictRuleLookup([
            new ConflictResolutionRule
            {
                EntityId = id,
                ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"The Hobbit"}"""),
                IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"name":"The Hobbit Trilogy"}"""),
                Fields = [new ConflictResolutionFieldRule { Field = "name", Resolution = FieldResolutionChoice.Keep }],
            },
        ]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            series: [new SeriesEntryDto { Id = id, Name = "The Hobbit Trilogy" }], conflictRules: rules);

        var seriesAction = actions.Single(a => a.EntityType == "Series");
        Assert.AreEqual(ImportActionStatus.Decided, seriesAction.Status.Parsed, "A matching rule must auto-resolve instead of leaving it Pending");
        var merged = System.Text.Json.JsonSerializer.Deserialize<SeriesActionPayloadDto>(seriesAction.MergedFields!)!;
        Assert.AreEqual("The Hobbit", merged.Name, "Keep must resolve to the existing side's value");
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
        var payload = System.Text.Json.JsonSerializer.Deserialize<SeriesActionPayloadDto>(seriesAction.IncomingValue!)!;
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
        var payload = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.IncomingValue!)!;
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
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.MergedFields!)!;
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
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.MergedFields!)!;
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
        using var conn = await OpenConnectionAsync();
        await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, "ce111111-1111-4111-8111-111111111111", title: "Casablanca", date: "1942", seriesId: null);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: "The Hobbit")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Pending, sourceAction.Status.Parsed, "No rule exists for this Source's seriesId enrichment under Review — regression guard matching pre-#181 behaviour for this site");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_ReviewPolicy_MatchingRule_StagesDecided()
    {
        using var conn = await OpenConnectionAsync();
        var hobbitSeriesId = await SeedExistingSeriesAsync(conn, "The Hobbit");
        var sourceId = "cf111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(conn, sourceId, title: "Casablanca", date: "1942", seriesId: null);
        var rules = new ConflictRuleLookup([
            new ConflictResolutionRule
            {
                EntityId = sourceId,
                ExistingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"seriesId":null}"""),
                IncomingRecord = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>($$"""{"seriesId":"{{hobbitSeriesId}}"}"""),
                Fields = [new ConflictResolutionFieldRule { Field = "seriesId", Resolution = FieldResolutionChoice.Replace }],
            },
        ]);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: "The Hobbit")], conflictRules: rules);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Decided, sourceAction.Status.Parsed, "A matching rule must auto-resolve the Source's seriesId enrichment instead of leaving it Pending");
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.MergedFields!)!;
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
        using var conn = await OpenConnectionAsync();
        await SeedExistingSeriesAsync(conn, "The Hobbit");
        await SeedExplicitSourceAsync(conn, "cd111111-1111-4111-8111-111111111111", title: "Casablanca", date: "1942", seriesId: null);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildEnrichmentEntry(title: "Casablanca", seriesName: "The Hobbit")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(sourceAction.MergedFields!)!;
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

    /// <summary>
    /// #175/developer decision (2026-07-24): Sql.Sources.SelectIdByTitleAndType/
    /// SelectExistingByTitleAndType are now case-insensitive — any input from an import file must
    /// match regardless of casing, so classifying an entry as new-vs-existing carries minimal
    /// friction and never risks a case-only duplicate.
    /// </summary>
    [TestMethod]
    public async Task PlanSourcesAsync_NoExplicitId_DifferingCasing_MatchesExistingNaturalKey()
    {
        using var conn = await OpenConnectionAsync();
        await SeedExplicitSourceAsync(conn, "d1111111-1111-4111-8111-111111111111", title: "Casablanca", type: "Movie");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildEnrichmentEntry(title: "CASABLANCA", type: Core.Enums.QuoteType.Movie, seriesName: null)]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source"), "Differing casing must still match the existing row by natural key, not stage a duplicate Add");
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
            sources: [BuildSourceEntry(id, title: "Casablanca", type: Core.Enums.QuoteType.Movie, date: "1942")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source"), "Nothing differs — silent reuse, no action staged");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NoIdMatch_FallsBackToNaturalKey_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        // A pre-existing row found only by natural key (Title+Type) — never declared an explicit id before.
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("INSERT INTO Quotinator_Source (Id, Title, Type, DateCreated) VALUES (@Id, 'Casablanca', 'Movie', @now)",
            new { Id = Guid.NewGuid(), now });

        var newFileId = "c3111111-1111-4111-8111-111111111111";
        // #190: date must be passed explicitly as null here — BuildSourceEntry's own default ("1942")
        // would otherwise now genuinely take effect on the natural-key path (requirement 6's
        // liberalization), which is a different, separately-tested scenario, not what this test means
        // to exercise (nothing about this entry differs from the existing row at all).
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(newFileId, title: "Casablanca", type: Core.Enums.QuoteType.Movie, date: null)]);

        Assert.IsEmpty(actions, "Not-yet-migrated row found via natural key — no re-keying, nothing staged (#162 scope boundary)");
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
        // #209/#210: Add uses the file's own declared id, canonicalized at capture (ADR 012) — not an
        // EntityIdentity-derived stable id, and no longer the file's raw casing verbatim.
        Assert.AreEqual(newFileId, sourceAction.EntityId);
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

        Assert.ContainsSingle(a => a.EntityType == "Source", actions, "Only one Source Add — the quote must resolve to the same row the sources[] section staged, not a second one");
        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
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
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_StageDirection (Id, Text, ImageUrl, CompletenessStatus, DateCreated) VALUES (@Id, @Text, @ImageUrl, @CompletenessStatus, @now)",
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

    private static SourceSoundCueDto BuildSoundCueEntry(string id, string text = "Distant thunder.", string? soundFileUrl = null, string? imageUrl = null) => new()
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
            "INSERT INTO Quotinator_SoundCue (Id, Text, SoundFileUrl, ImageUrl, CompletenessStatus, DateCreated) VALUES (@Id, @Text, @SoundFileUrl, @ImageUrl, @CompletenessStatus, @now)",
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

    private static PersonEntryDto BuildPersonEntry(string id, string name = "Ada Lovelace", string? dateOfBirth = "1815-12-10", string? dateOfDeath = "1852-11-27") => new()
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
            "INSERT INTO Quotinator_Person (Id, Name, DateOfBirth, DateOfDeath, CompletenessStatus, DateCreated) VALUES (@Id, @Name, @DateOfBirth, @DateOfDeath, @CompletenessStatus, @now)",
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
        await conn.ExecuteAsync("INSERT INTO Quotinator_Person (Id, Name, DateCreated) VALUES (@Id, 'Ada Lovelace', @now)",
            new { Id = Guid.NewGuid(), now });

        var newFileId = "e3111111-1111-4111-8111-111111111173";
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(newFileId, name: "Ada Lovelace")]);

        Assert.IsEmpty(actions, "Not-yet-migrated row found via natural key — no re-keying, nothing staged (#173 scope boundary, same as #162's)");
    }

    /// <summary>
    /// #216 fix: Sql.People.SelectIdByName is now case-insensitive, matching #180's own
    /// Sql.Sources.SelectIdByTitleAndType precedent — a case-only difference must still find the
    /// existing row via natural key, not stage a duplicate Add.
    /// </summary>
    [TestMethod]
    public async Task PlanPeopleAsync_NoIdMatch_DifferingCasing_FallsBackToNaturalKey_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("INSERT INTO Quotinator_Person (Id, Name, DateCreated) VALUES (@Id, 'Ada Lovelace', @now)",
            new { Id = Guid.NewGuid(), now });

        var newFileId = "e3211111-1111-4111-8111-111111111173";
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(newFileId, name: "ADA LOVELACE")]);

        Assert.IsEmpty(actions, "Differing casing must still match the existing row via natural key, not stage a duplicate Add");
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
        using var conn = await OpenConnectionAsync();
        var sourceId = await SeedSourceAsync(conn, "Existing Film");
        var characterId = await SeedGlobalCharacterAsync(conn, "Gandalf", sourceId, "Movie");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(characterId, name: "Gandalf the Grey")]);

        var action = actions.Single(a => a.EntityType == "Character");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed);
        Assert.IsNotNull(action.MergedFields);
    }

    [TestMethod]
    public async Task PlanCharactersAsync_IdMatchFound_NothingChanged_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var sourceId = await SeedSourceAsync(conn, "Existing Film");
        var characterId = await SeedGlobalCharacterAsync(conn, "Gandalf", sourceId, "Movie");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(characterId, name: "Gandalf")]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Character"), "Nothing differs — silent reuse, no action staged");
    }

    [TestMethod]
    public async Task PlanCharactersAsync_IdDoesNotMatch_FallsBackToSameSourceCandidate_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var sourceId = await SeedSourceAsync(conn, "Existing Film");
        await SeedGlobalCharacterAsync(conn, "Gandalf", sourceId, "Movie");
        var bogusId = "aaaaaaaa-1111-4111-8111-111111111111";

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(bogusId, name: "Gandalf", sourceTitle: "Existing Film", sourceType: Core.Enums.QuoteType.Movie)]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Character"), "A declared id that matches nothing must fall back to ADR 013's real matching algorithm, same as PlanSourcesAsync's own id-not-found fallback");
    }

    [TestMethod]
    public async Task PlanCharactersAsync_NoIdMatch_SeriesScopedCandidateFound_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var seriesId = await SeedExistingSeriesAsync(conn, "LOTR Trilogy");
        var source1Id = await SeedSourceAsync(conn, "The Fellowship of the Ring", seriesId: seriesId);
        await SeedGlobalCharacterAsync(conn, "Aragorn", source1Id, "Movie");
        await SeedSourceAsync(conn, "The Two Towers", seriesId: seriesId);

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(null, name: "Aragorn", sourceTitle: "The Two Towers", sourceType: Core.Enums.QuoteType.Movie)]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Character"), "A Series-scoped cross-Source candidate must be reused directly, matching ResolveCharacterAsync's own behaviour");
    }

    [TestMethod]
    public async Task PlanCharactersAsync_NoIdMatch_NoCandidateFound_StagesAddAction()
    {
        using var conn = await OpenConnectionAsync();
        await SeedSourceAsync(conn, "A Brand New Film");

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(null, name: "A Brand New Character", sourceTitle: "A Brand New Film", sourceType: Core.Enums.QuoteType.Movie)]);

        var action = actions.Single(a => a.EntityType == "Character");
        Assert.AreEqual(ImportActionKind.Add, action.ActionType.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed);
    }

    [TestMethod]
    public async Task PlanCharactersAsync_NoIdMatch_SourceDoesNotExistYet_StagesBothSourceAndCharacterAdds()
    {
        using var conn = await OpenConnectionAsync();

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(null, name: "A Brand New Character", sourceTitle: "A Never-Before-Seen Film", sourceType: Core.Enums.QuoteType.Movie)]);

        Assert.ContainsSingle(a => a.EntityType == "Source", actions, "The referenced Source must be resolved/created too, same as a quote's own ResolveSourceAsync");
        var characterAction = actions.Single(a => a.EntityType == "Character");
        Assert.AreEqual(ImportActionKind.Add, characterAction.ActionType.Parsed);
    }

    [TestMethod]
    public async Task PlanCharactersAsync_CompleteStatus_StagesBlockedNotModify()
    {
        using var conn = await OpenConnectionAsync();
        var sourceId = await SeedSourceAsync(conn, "Existing Film");
        var characterId = await SeedGlobalCharacterAsync(conn, "Gandalf", sourceId, "Movie");
        await conn.ExecuteAsync("UPDATE Quotinator_Character SET CompletenessStatus = 'Complete' WHERE Id = @id", new { id = characterId });

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            characters: [BuildCharacterEntry(characterId, name: "A completely different name")]);

        var action = actions.Single(a => a.EntityType == "Character");
        Assert.AreEqual(ImportActionStatus.Blocked, action.Status.Parsed, "A Complete row must never silently accept a Modify");
        Assert.IsNull(action.MergedFields, "Nothing is resolved yet for a Blocked action");
    }

    [TestMethod]
    public async Task PlanCharactersAsync_CompleteStatus_SkipPolicy_DoesNotBlock()
    {
        using var conn = await OpenConnectionAsync();
        var sourceId = await SeedSourceAsync(conn, "Existing Film");
        var characterId = await SeedGlobalCharacterAsync(conn, "Gandalf", sourceId, "Movie");
        await conn.ExecuteAsync("UPDATE Quotinator_Character SET CompletenessStatus = 'Complete' WHERE Id = @id", new { id = characterId });

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.Skip,
            characters: [BuildCharacterEntry(characterId, name: "A completely different name")]);

        var action = actions.Single(a => a.EntityType == "Character");
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
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Conversation (Id, Description, CompletenessStatus, DateCreated) VALUES (@Id, @Description, @CompletenessStatus, @now)",
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

        var entry = new SourceConversationDto
        {
            Id          = id,
            Description = "A tense standoff in the saloon.",
            Lines       = [new SourceConversationLineDto { Order = 0, Type = Core.Enums.ConversationLineType.Quote, QuoteId = "11111111-1111-4111-8111-111111111111" }],
        };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins, conversations: [entry]);

        var action = actions.Single(a => a.EntityType == "Conversation");
        var merged = System.Text.Json.JsonSerializer.Deserialize<ConversationActionPayloadDto>(action.MergedFields!)!;
        Assert.IsEmpty(merged.Lines, "Lines are never read or included in a Modify payload — out of scope for this issue");
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

    // ── #209: canonicalize explicit ids at capture ───────────────────────────

    [TestMethod]
    public async Task PlanSourcesAsync_UppercaseExplicitId_AddPath_ResolvedIdIsCanonicalLowercase()
    {
        using var conn = await OpenConnectionAsync();
        var uppercaseId = "C8111111-1111-4111-8111-111111111177";

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(uppercaseId, title: "A Brand New Film (Canonical Id Test)")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(uppercaseId.ToLowerInvariant(), sourceAction.EntityId, "An uppercase file-authored explicit id must canonicalize to lowercase at capture");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_UppercaseExplicitId_CorrectionMatch_IndexedIdIsCanonicalLowercase()
    {
        using var conn = await OpenConnectionAsync();
        var canonicalId = "c9111111-1111-4111-8111-111111111178";
        await SeedExplicitSourceAsync(conn, canonicalId, title: "Casablanca");
        var quote = BuildQuote("ca111111-1111-4111-8111-111111111179", source: "Casablanca (Corrected)");

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(canonicalId.ToUpperInvariant(), title: "Casablanca (Corrected)")]);

        Assert.ContainsSingle(a => a.EntityType == "Source", actions, "The correction-match must be found via case-insensitive lookup — no duplicate Add");
        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual(canonicalId, payload.SourceId, "sourceIndex must be seeded with the canonicalized (lowercase) form of the file's uppercase id, not the raw file casing, so a same-batch quote resolves to the row's real stored id");
    }

    [TestMethod]
    public async Task PlanSourcesAsync_QuoteReferencesUppercaseExplicitSource_ResolvedSourceIdIsCanonical()
    {
        using var conn = await OpenConnectionAsync();
        var uppercaseId = "CB111111-1111-4111-8111-111111111180";
        var quote = BuildQuote("cc111111-1111-4111-8111-111111111181", source: "A Brand New Film (Join Canonical Test)");

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [BuildSourceEntry(uppercaseId, title: "A Brand New Film (Join Canonical Test)")]);

        var sourceAction = actions.Single(a => a.EntityType == "Source");
        var quoteAction  = actions.Single(a => a.EntityType == "Quote");
        var payload = System.Text.Json.JsonSerializer.Deserialize<QuoteActionPayloadDto>(quoteAction.IncomingValue!)!;
        Assert.AreEqual(sourceAction.EntityId, payload.SourceId, "The quote must resolve to the same canonical id the Source Add itself staged");
        Assert.AreEqual(uppercaseId.ToLowerInvariant(), payload.SourceId);
    }

    [TestMethod]
    public async Task PlanPeopleAsync_UppercaseExplicitId_ResolvedIdIsCanonicalLowercase()
    {
        using var conn = await OpenConnectionAsync();
        var uppercaseId = "E4111111-1111-4111-8111-111111111174";

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [BuildPersonEntry(uppercaseId, name: "A Brand New Person (Canonical Id Test)")]);

        var personAction = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(uppercaseId.ToLowerInvariant(), personAction.EntityId, "An uppercase file-authored explicit id must canonicalize to lowercase at capture");
    }

    [TestMethod]
    public async Task PlanStageDirectionsAsync_UppercaseExplicitId_ResolvedIdIsCanonicalLowercase()
    {
        using var conn = await OpenConnectionAsync();
        var uppercaseId = "DC111111-1111-4111-8111-111111111177";

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            stageDirections: [BuildStageDirectionEntry(uppercaseId, text: "A brand new stage direction (canonical id test).")]);

        var action = actions.Single(a => a.EntityType == "StageDirection");
        Assert.AreEqual(uppercaseId.ToLowerInvariant(), action.EntityId, "An uppercase file-authored explicit id must canonicalize to lowercase at capture");
    }

    [TestMethod]
    public async Task PlanSoundCuesAsync_UppercaseExplicitId_ResolvedIdIsCanonicalLowercase()
    {
        using var conn = await OpenConnectionAsync();
        var uppercaseId = "DD111111-1111-4111-8111-111111111178";

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            soundCues: [BuildSoundCueEntry(uppercaseId, text: "A brand new sound cue (canonical id test).")]);

        var action = actions.Single(a => a.EntityType == "SoundCue");
        Assert.AreEqual(uppercaseId.ToLowerInvariant(), action.EntityId, "An uppercase file-authored explicit id must canonicalize to lowercase at capture");
    }

    [TestMethod]
    public async Task PlanConversationsAsync_UppercaseExplicitId_ResolvedIdIsCanonicalLowercase()
    {
        using var conn = await OpenConnectionAsync();
        var uppercaseId = "DE111111-1111-4111-8111-111111111179";

        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            conversations: [BuildConversationEntry(uppercaseId, description: "A brand new conversation (canonical id test).")]);

        var action = actions.Single(a => a.EntityType == "Conversation");
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
        using var conn = await OpenConnectionAsync();
        var uppercaseId = "DF111111-1111-4111-8111-111111111180";
        var quote = BuildQuote(uppercaseId, source: "A Brand New Film (Quote Canonical Id Test)");

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins);

        var quoteAction = actions.Single(a => a.EntityType == "Quote");
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
        using var conn = await OpenConnectionAsync();
        var uppercaseQuoteId = "DF222222-2222-4222-8222-222222222280";
        var quote = BuildQuote(uppercaseQuoteId, source: "A Film With A Referenced Line (Canonical Id Test)");
        var conversationEntry = new SourceConversationDto
        {
            Id          = "df333333-3333-4333-8333-333333333380",
            Description = "A conversation referencing an uppercase-authored quote id.",
            Lines       = [new SourceConversationLineDto { Order = 0, Type = Core.Enums.ConversationLineType.Quote, QuoteId = uppercaseQuoteId }],
        };

        var actions = await ImportActionPlanner.PlanAsync(conn, [quote], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            conversations: [conversationEntry]);

        var conversationAction = actions.Single(a => a.EntityType == "Conversation");
        var payload = System.Text.Json.JsonSerializer.Deserialize<ConversationActionPayloadDto>(conversationAction.IncomingValue!)!;
        Assert.AreEqual(uppercaseQuoteId.ToLowerInvariant(), payload.Lines[0].QuoteId,
            "A conversation line's QuoteId must be canonicalized to lowercase, matching the referenced quote's own canonical id — otherwise the ConversationLines FOREIGN KEY constraint to Quotes(Id) fails");
    }

    // ── #190: absent vs. explicit-null distinguishability ────────────────────

    [TestMethod]
    public async Task PlanSourcesAsync_ExplicitId_DateAbsent_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e0111111-1111-4111-8111-111111111181";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", date: "1942");

        var entry = new SourceEntryDto { Id = id, Title = "Casablanca", Type = Core.Enums.QuoteType.Movie };
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

        var entry = new SourceEntryDto { Id = id, Title = "Casablanca", Type = Core.Enums.QuoteType.Movie, Date = Optional<string>.Of(null) };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [entry]);

        var action = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed, "An explicit 'date: null' must resolve to a genuine reset");
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(action.MergedFields!)!;
        Assert.IsNull(merged.Date);
    }

    [TestMethod]
    public async Task PlanSourcesAsync_ExplicitId_SeriesNameAbsent_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var seriesId = await SeedExistingSeriesAsync(conn, "The Hobbit");
        var id = "e0111111-1111-4111-8111-111111111183";
        await SeedExplicitSourceAsync(conn, id, title: "Casablanca", date: "1942", seriesId: seriesId);

        var entry = new SourceEntryDto { Id = id, Title = "Casablanca", Type = Core.Enums.QuoteType.Movie, Date = "1942" };
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

        var entry = new SourceEntryDto { Id = id, Title = "Casablanca", Type = Core.Enums.QuoteType.Movie, Date = "1942", SeriesName = Optional<string>.Of(null) };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [entry]);

        var action = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed, "An explicit 'seriesName: null' must resolve to a genuine clear");
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(action.MergedFields!)!;
        Assert.IsNull(merged.SeriesId);
    }

    [TestMethod]
    public async Task PlanSourcesAsync_NaturalKey_DateExplicitlySet_NowTakesEffect()
    {
        using var conn = await OpenConnectionAsync();
        // Not referenced by the entry below — a row found only by natural key (Title+Type).
        await SeedExplicitSourceAsync(conn, "e0111111-1111-4111-8111-111111111185", title: "Casablanca", date: null);

        // #180's enrichment shape: no explicit id.
        var entry = new SourceEntryDto { Title = "Casablanca", Type = Core.Enums.QuoteType.Movie, Date = "1975" };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [entry]);

        var action = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed,
            "#190 requirement 6's liberalization: a natural-key entry that explicitly sets 'date' now actually takes effect, where it was previously always silently ignored regardless of what the file said");
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(action.MergedFields!)!;
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

        var entry = new SourceEntryDto { Title = "Casablanca", Type = Core.Enums.QuoteType.Movie, SeriesName = "New Series" };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.MergeOurs,
            sources: [entry]);

        var action = actions.Single(a => a.EntityType == "Source");
        var merged = System.Text.Json.JsonSerializer.Deserialize<SourceActionPayloadDto>(action.MergedFields!)!;
        Assert.AreEqual(originalSeriesId, merged.SeriesId,
            "#190 drive-by fix: MergeOurs must keep the existing Series on a genuine conflict — this branch previously never consulted FieldMergeResolver at all and always took the incoming value unconditionally");
    }

    [TestMethod]
    public async Task PlanPeopleAsync_DateOfBirthAbsent_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e0111111-1111-4111-8111-111111111187";
        await SeedExplicitPersonAsync(conn, id, name: "Ada Lovelace", dateOfBirth: "1815-12-10", dateOfDeath: "1852-11-27");

        var entry = new PersonEntryDto { Id = id, Name = "Ada Lovelace", DateOfDeath = "1852-11-27" };
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

        var entry = new PersonEntryDto { Id = id, Name = "Ada Lovelace", DateOfBirth = "1815-12-10", DateOfDeath = Optional<string>.Of(null) };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [entry]);

        var action = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(ImportActionKind.Modify, action.ActionType.Parsed, "An explicit 'dateOfDeath: null' must resolve to a genuine reset");
        var merged = System.Text.Json.JsonSerializer.Deserialize<PersonActionPayloadDto>(action.MergedFields!)!;
        Assert.IsNull(merged.DateOfDeath);
    }

    [TestMethod]
    public async Task PlanStageDirectionsAsync_ImageUrlAbsent_NoActionStaged()
    {
        using var conn = await OpenConnectionAsync();
        var id = "e0111111-1111-4111-8111-111111111189";
        await SeedExplicitStageDirectionAsync(conn, id, text: "A shot rings out.", imageUrl: "http://example.com/still.jpg");

        var entry = new SourceStageDirectionDto { Id = id, Text = "A shot rings out." };
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

        var entry = new SourceSoundCueDto { Id = id, Text = "Distant thunder.", ImageUrl = "http://example.com/img.jpg" };
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

        var entry = new SourceConversationDto { Id = id, Lines = [] };
        var actions = await ImportActionPlanner.PlanAsync(conn, [], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            conversations: [entry]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Conversation"), "An omitted 'description' must never be treated as a change, under any policy");
    }
}
