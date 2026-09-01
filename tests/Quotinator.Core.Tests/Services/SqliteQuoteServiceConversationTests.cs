using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Models;
using Quotinator.Core.Queries;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Services;

namespace Quotinator.Core.Tests.Services;

/// <summary>
/// SQLite integration tests for #69's conversation-aware API surface —
/// <see cref="SqliteQuoteService.GetConversation"/>, <c>QuoteResponse.Conversations</c> membership
/// on every read call, and <see cref="SqliteQuoteService.GetRandom"/>'s dedup/embed behaviour.
/// Seeds a real extended-format fixture: two quotes in one quote-only conversation (dedup target),
/// one quote in a second conversation mixed with a stage direction and sound cue (full line-shape
/// coverage, including a Dutch translation on each), and one standalone quote in no conversation.
/// </summary>
[TestClass]
public class SqliteQuoteServiceConversationTests
{
    private const string QuoteAId          = "eeeeeeee-0000-0000-0000-000000000001";
    private const string QuoteBId          = "eeeeeeee-0000-0000-0000-000000000002";
    private const string QuoteStandaloneId = "eeeeeeee-0000-0000-0000-000000000003";
    private const string QuoteDId          = "eeeeeeee-0000-0000-0000-000000000004";
    private const string StageDirectionId  = "eeeeeeee-0000-0000-0000-000000000005";
    private const string SoundCueId        = "eeeeeeee-0000-0000-0000-000000000006";
    private const string Conversation1Id   = "eeeeeeee-0000-0000-0000-00000000000a";
    private const string Conversation2Id   = "eeeeeeee-0000-0000-0000-00000000000b";

    private string _tempDir = null!;
    private string _dbPath  = null!;
    private string _backups = null!;
    private string _fixture = null!;

    private IDbConnectionFactory _factory = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_conversation_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
        _backups = Path.Combine(_tempDir, "backups");
        _fixture = Path.Combine(_tempDir, "conversation-fixture.json");

        File.WriteAllText(_fixture, $$"""
            {
              "quotes": [
                { "id": "{{QuoteAId}}", "quote": "Have you ever been in a cockpit before?", "source": "Airplane!", "type": "movie", "character": "Roger Murdock" },
                { "id": "{{QuoteBId}}", "quote": "No sir, I've never been up in a plane before.", "source": "Airplane!", "type": "movie", "character": "Joey" },
                { "id": "{{QuoteStandaloneId}}", "quote": "A standalone quote in no conversation.", "source": "Somewhere", "type": "movie" },
                { "id": "{{QuoteDId}}", "quote": "You ever seen a grown man naked?", "source": "Airplane!", "type": "movie", "character": "Captain Oveur" }
              ],
              "stageDirections": [
                { "id": "{{StageDirectionId}}", "text": "[EXT. COCKPIT]", "translations": { "nl": { "text": "[EXT. COCKPIT NL]" } } }
              ],
              "soundCues": [
                { "id": "{{SoundCueId}}", "text": "[awkward silence]", "translations": { "nl": { "text": "[pijnlijke stilte]" } } }
              ],
              "conversations": [
                {
                  "id": "{{Conversation1Id}}",
                  "lines": [
                    { "order": 1, "type": "quote", "quoteId": "{{QuoteAId}}" },
                    { "order": 2, "type": "quote", "quoteId": "{{QuoteBId}}" }
                  ]
                },
                {
                  "id": "{{Conversation2Id}}",
                  "description": "Cockpit scene",
                  "lines": [
                    { "order": 1, "type": "stage_direction", "stageDirectionId": "{{StageDirectionId}}" },
                    { "order": 2, "type": "quote", "quoteId": "{{QuoteDId}}" },
                    { "order": 3, "type": "sound_cue", "soundCueId": "{{SoundCueId}}" }
                  ]
                }
              ]
            }
            """);

        _factory = new SqliteConnectionFactory(_dbPath);
        DatabaseOptions options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        SqliteImportBatchRepository importBatches = new SqliteImportBatchRepository(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        NullLogger<DatabaseInitializer> logger        = NullLogger<DatabaseInitializer>.Instance;
        SeedBatch batch         = new SeedBatch([new SeedFile(_fixture, null)], ManifestPolicy.HardcodedDefault, "conversation-fixture");
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
        QuotinatorDatabaseInitializer db = new QuotinatorDatabaseInitializer(_factory, options, QuotinatorMigrations.All, [batch], importBatches,
            coordinator, actionService, actionWriter,
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, logger,
            NoOpSourceCacheUpdater.Instance, autoUpdateSources: false,
            autoPurgeBundledImportActions: false, autoPurgeUserImportActions: false,
            NoOpRuleFileOverridePathResolver.Instance, NoOpSourceFileOverrideRegistry.Instance, NoOpFileResourceRepository.Instance,
            NoOpNotificationReader.Instance, NoOpNotificationWriter.Instance, NoOpNotificationTextSource.Instance,
            new AppVersionTracker(_factory), new VersionService(), NoOpDiskSpaceProvider.Instance);
        await db.InitialiseAsync();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private SqliteQuoteService CreateService() => new(
        _factory,
        unicodeAwareSearch: false,
        new JoinQueryRepository<QuoteRow>(_factory, new QuoteLineStrategy()),
        new JoinQueryRepository<StageDirectionLineRow>(_factory, new StageDirectionLineStrategy()),
        new JoinQueryRepository<SoundCueLineRow>(_factory, new SoundCueLineStrategy()));

    // ── QuoteResponse.Conversations membership ──────────────────────────────

    [TestMethod]
    public async Task GetById_QuoteInOneConversation_PopulatesConversationsMembership()
    {
        QuoteResponse? quote = await CreateService().GetById(QuoteAId);

        Assert.IsNotNull(quote!.Conversations);
        Assert.HasCount(1, quote.Conversations!);
        Assert.AreEqual(Conversation1Id, quote.Conversations![0].ConversationId);
        Assert.AreEqual(1, quote.Conversations[0].Position);
        Assert.AreEqual(2, quote.Conversations[0].TotalLines);
    }

    [TestMethod]
    public async Task GetById_QuoteInNoConversation_ConversationsIsNull()
    {
        QuoteResponse? quote = await CreateService().GetById(QuoteStandaloneId);

        Assert.IsNull(quote!.Conversations);
    }

    [TestMethod]
    public async Task GetById_EmbeddedConversationField_IsAlwaysNull()
    {
        // Only /random ever populates EmbeddedConversation — GetById never does, regardless of membership.
        QuoteResponse? quote = await CreateService().GetById(QuoteAId);

        Assert.IsNull(quote!.EmbeddedConversation);
    }

    // ── GetConversation ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetConversation_UnknownId_ReturnsNull()
    {
        Assert.IsNull(await CreateService().GetConversation("00000000-0000-0000-0000-000000000000"));
    }

    [TestMethod]
    public async Task GetConversation_CaseInsensitiveId_StillResolves()
    {
        ConversationResponse? result = await CreateService().GetConversation(Conversation1Id.ToUpperInvariant());

        Assert.IsNotNull(result);
        // #209/#210: the Conversation was seeded through the real import pipeline, so its id is
        // canonicalized at capture — the lowercase fixture constant already matches the stored form
        // (ADR 012), so the lookup above deliberately passes an uppercase-cased id to prove the
        // case-insensitive lookup itself still resolves it.
        Assert.AreEqual(Conversation1Id, result!.Id);
    }

    [TestMethod]
    public async Task GetConversation_QuoteOnlyConversation_ReturnsLinesInOrder()
    {
        ConversationResponse? result = await CreateService().GetConversation(Conversation1Id);

        Assert.IsNotNull(result);
        Assert.HasCount(2, result!.Lines);
        Assert.AreEqual(1, result.Lines[0].Order);
        Assert.AreEqual("quote", result.Lines[0].Type);
        Assert.AreEqual(QuoteAId, result.Lines[0].Quote!.Id);
        Assert.AreEqual(2, result.Lines[1].Order);
        Assert.AreEqual(QuoteBId, result.Lines[1].Quote!.Id);
    }

    [TestMethod]
    public async Task GetConversation_EmbeddedQuoteLine_HasNoRecursiveConversationsField()
    {
        ConversationResponse? result = await CreateService().GetConversation(Conversation1Id);

        Assert.IsNull(result!.Lines[0].Quote!.Conversations);
        Assert.IsNull(result.Lines[0].Quote!.EmbeddedConversation);
    }

    [TestMethod]
    public async Task GetConversation_MixedLineTypes_ReturnsCorrectShapePerType()
    {
        ConversationResponse? result = await CreateService().GetConversation(Conversation2Id);

        Assert.AreEqual("Cockpit scene", result!.Description);
        Assert.HasCount(3, result.Lines);

        ConversationLineResponse stageDirectionLine = result.Lines[0];
        Assert.AreEqual("stage_direction", stageDirectionLine.Type);
        Assert.AreEqual("[EXT. COCKPIT]", stageDirectionLine.Text);
        Assert.AreEqual("en", stageDirectionLine.Language);
        Assert.IsFalse(stageDirectionLine.IsTranslated);
        Assert.IsNull(stageDirectionLine.Quote);

        ConversationLineResponse quoteLine = result.Lines[1];
        Assert.AreEqual("quote", quoteLine.Type);
        Assert.AreEqual(QuoteDId, quoteLine.Quote!.Id);

        ConversationLineResponse soundCueLine = result.Lines[2];
        Assert.AreEqual("sound_cue", soundCueLine.Type);
        Assert.AreEqual("[awkward silence]", soundCueLine.Text);
        Assert.IsNull(soundCueLine.SoundFileUrl);
    }

    [TestMethod]
    public async Task GetConversation_LangNl_TranslatesStageDirectionAndSoundCueText()
    {
        ConversationResponse? result = await CreateService().GetConversation(Conversation2Id, "nl");

        ConversationLineResponse stageDirectionLine = result!.Lines[0];
        Assert.AreEqual("[EXT. COCKPIT NL]", stageDirectionLine.Text);
        Assert.AreEqual("nl", stageDirectionLine.Language);
        Assert.IsTrue(stageDirectionLine.IsTranslated);

        ConversationLineResponse soundCueLine = result.Lines[2];
        Assert.AreEqual("[pijnlijke stilte]", soundCueLine.Text);
        Assert.AreEqual("nl", soundCueLine.Language);
        Assert.IsTrue(soundCueLine.IsTranslated);
    }

    [TestMethod]
    public async Task GetConversation_LangRequestedButNoTranslationExists_FallsBackToOriginal()
    {
        // "de" has no translation for the stage direction/sound cue in this fixture.
        ConversationResponse? result = await CreateService().GetConversation(Conversation2Id, "de");

        Assert.AreEqual("[EXT. COCKPIT]", result!.Lines[0].Text);
        Assert.AreEqual("en", result.Lines[0].Language);
        Assert.IsFalse(result.Lines[0].IsTranslated);
    }

    // ── GetRandom conversation-aware dedup ──────────────────────────────────

    [TestMethod]
    public async Task GetRandom_RepeatedCallsForConversationQuote_NeverReturnsBothQuotesFromSameConversationTogether()
    {
        SqliteQuoteService service = CreateService();

        // Deterministically force both conversation-1 quotes into the pool alongside nothing else,
        // by requesting n=2 restricted to Airplane! quotes that aren't QuoteD (character filter
        // isolates Roger Murdock/Joey, conversation 1's own two lines).
        for (int i = 0; i < 20; i++)
        {
            FilteredQuoteResult<QuoteResponse> result = await service.GetRandom(2, source: "Airplane!");
            List<string> conversationIds = [.. result.Items
                .Where(q => q.Conversations is not null)
                .SelectMany(q => q.Conversations!)
                .Select(m => m.ConversationId)];

            Assert.AreAllDistinct(conversationIds, "The same conversation must never be represented by two different quotes in one /random result");
        }
    }

    [TestMethod]
    public async Task GetRandom_QuoteInConversation_EmbedsFullConversationOnThatItem()
    {
        SqliteQuoteService service = CreateService();
        FilteredQuoteResult<QuoteResponse>? found = null;
        for (int i = 0; i < 20 && found is null; i++)
        {
            FilteredQuoteResult<QuoteResponse> result = await service.GetRandom(4, source: "Airplane!");
            if (result.Items.Any(q => q.EmbeddedConversation is not null))
                found = result;
        }

        Assert.IsNotNull(found, "Expected at least one /random draw to embed a conversation across 20 attempts");
        QuoteResponse withEmbed = found!.Items.First(q => q.EmbeddedConversation is not null);
        Assert.IsGreaterThanOrEqualTo(2, withEmbed.EmbeddedConversation!.Lines.Count);
    }

    [TestMethod]
    public async Task GetRandom_SetsRequestedAndReturnedCount()
    {
        FilteredQuoteResult<QuoteResponse> result = await CreateService().GetRandom(2, source: "Airplane!");

        Assert.AreEqual(2, result.RequestedCount);
        Assert.AreEqual(result.Items.Count, result.ReturnedCount);
    }

    [TestMethod]
    public async Task GetRandom_RequestMoreThanPoolAfterDedup_ReturnedCountReflectsShortfall()
    {
        // Only 4 Airplane! quotes exist total, 2 of which collapse into one conversation slot's
        // worth of exclusion the moment either conversation-1 quote is picked — requesting all 4
        // can legitimately return fewer once dedup removes a conversation partner from the pool.
        FilteredQuoteResult<QuoteResponse> result = await CreateService().GetRandom(4, source: "Airplane!");

        Assert.AreEqual(4, result.RequestedCount);
        Assert.IsTrue(result.ReturnedCount <= 4);
        Assert.AreEqual(result.Items.Count, result.ReturnedCount);
    }

    [TestMethod]
    public async Task Search_QuoteInConversation_PopulatesConversationsMembership()
    {
        FilteredQuoteResult<QuoteResponse> result = await CreateService().Search("cockpit before", 10, field: "quote");

        Assert.HasCount(1, result.Items);
        Assert.IsNotNull(result.Items[0].Conversations);
    }
}
