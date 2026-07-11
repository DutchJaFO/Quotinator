using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Database;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Models;
using Quotinator.Engine.Services;

namespace Quotinator.Engine.Tests.Services;

/// <summary>
/// Exercises <see cref="SqliteImportActionService"/> against a real, freshly-migrated SQLite schema
/// — the actual decide/apply/discard workflow that replaces #149's conflict-resolution service.
/// </summary>
[TestClass]
public class SqliteImportActionServiceTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private SqliteConnectionFactory _factory = null!;
    private ISystemImportActionReader _actionReader = null!;
    private ISystemImportActionWriter _actionWriter = null!;
    private IImportActionCoordinator _coordinator = null!;
    private SqliteImportActionService _service = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_action_service_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
        _factory = new SqliteConnectionFactory(_dbPath);

        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = Path.Combine(_tempDir, "backups") };
        var importBatches = new SqliteImportBatchRepository(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);

        _actionReader = new SystemImportActionReader(_factory);
        _actionWriter = new SystemImportActionWriter(_factory);
        _coordinator  = new ImportActionResolutionCoordinator(_actionReader, _actionWriter, _factory);
        _service      = new SqliteImportActionService(_actionReader, _coordinator, new SystemChangeLogWriter(_factory),
            new SqliteRestorableRepository<QuoteEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Source>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Character>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Person>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            importBatches, _factory);

        var db = new QuotinatorDatabaseInitializer(_factory, options, QuotinatorMigrations.All, [], importBatches,
            _coordinator, _service, NoOpSystemAuditWriter.Instance,
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

    private static SourceQuote BuildQuote(string id, string source = "Casablanca", string? character = "Rick Blaine", string quoteText = "Here's looking at you, kid.", string[]? genres = null) => new()
    {
        Id               = id,
        QuoteText        = quoteText,
        OriginalLanguage = "en",
        Source           = source,
        Character        = character,
        Type             = QuoteType.Movie,
        Genres           = genres ?? [],
    };

    private async Task<IReadOnlyList<SystemImportAction>> PlanAndStageAsync(
        IReadOnlyList<SourceQuote> quotes, Guid batchId, DuplicateResolutionPolicy policy,
        IReadOnlyList<SourceStageDirection>? stageDirections = null,
        IReadOnlyList<SourceSoundCue>? soundCues = null,
        IReadOnlyList<SourceConversation>? conversations = null)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        // Sources.ImportBatchId (and Characters/People/Quotes) is a real FK to ImportBatches — the
        // applier's writes need a genuine row to reference, same as production's own batch-first flow.
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, DateCreated) VALUES (@Id, 'test', 'Import', @now, @now)",
            new { Id = batchId, now });

        var actions = await ImportActionPlanner.PlanAsync(conn, quotes, batchId, policy, stageDirections: stageDirections, soundCues: soundCues, conversations: conversations);
        await _coordinator.StageAsync(actions);
        return actions;
    }

    [TestMethod]
    public async Task DecideAsync_NonQuoteAction_ThrowsImportActionNotDecidableException()
    {
        var batchId = Guid.NewGuid();
        var actions = await PlanAndStageAsync([BuildQuote("11111111-1111-4111-8111-111111111111")], batchId, DuplicateResolutionPolicy.NewestWins);
        var sourceAction = actions.Single(a => a.EntityType == "Source");

        await Assert.ThrowsExactlyAsync<ImportActionNotDecidableException>(
            () => _service.DecideAsync(sourceAction.Id, new ConflictDecisionRequest()));
    }

    [TestMethod]
    public async Task DecideAsync_UnknownId_ThrowsImportActionNotFoundException()
        => await Assert.ThrowsExactlyAsync<ImportActionNotFoundException>(
            () => _service.DecideAsync(Guid.NewGuid(), new ConflictDecisionRequest()));

    [TestMethod]
    public async Task DecideAsync_AmbiguousFieldLeftUndecided_ThrowsUnresolvedFieldConflictException()
    {
        var id = "21111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(id, "Original text");

        var actions = await PlanAndStageAsync([BuildQuote(id)], Guid.NewGuid(), DuplicateResolutionPolicy.Review);
        var quoteAction = actions.Single(a => a.EntityType == "Quote");

        await Assert.ThrowsExactlyAsync<UnresolvedFieldConflictException>(
            () => _service.DecideAsync(quoteAction.Id, new ConflictDecisionRequest()));
    }

    [TestMethod]
    public async Task DecideAsync_AllFieldsDecided_TransitionsToDecidedWithResolvedMergedFields()
    {
        var id = "31111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(id, "Original text");

        var actions = await PlanAndStageAsync([BuildQuote(id)], Guid.NewGuid(), DuplicateResolutionPolicy.Review);
        var quoteAction = actions.Single(a => a.EntityType == "Quote");

        await _service.DecideAsync(quoteAction.Id, new ConflictDecisionRequest
        {
            QuoteText = new FieldDecision { Choice = FieldResolutionChoice.Replace },
        });

        var found = await _actionReader.GetByIdAsync(quoteAction.Id);
        Assert.AreEqual(ImportActionStatus.Decided, found!.Status.Parsed);
        Assert.IsNotNull(found.MergedFields);
    }

    [TestMethod]
    public async Task ApplyBatchAsync_BrandNewQuote_WritesQuoteAndSourceRows()
    {
        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([BuildQuote("41111111-1111-4111-8111-111111111111")], batchId, DuplicateResolutionPolicy.NewestWins);

        var result = await _service.ApplyBatchAsync(batchId.ToString("D").ToUpperInvariant());

        Assert.IsNull(result, "Nothing pending — the whole batch must apply");
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes"));
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sources"));
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Characters"));
    }

    /// <summary>
    /// Regression guard (found during T1 manual testing): <c>ImportResultResponse.BatchId</c> is a
    /// <c>Guid</c>, which .NET serializes lowercase by default, but stored <c>BatchId</c> values are
    /// always uppercase. A caller round-tripping the batch id straight from that response — the
    /// documented workflow — must still find and apply the batch, not silently match nothing and
    /// report success having applied zero actions.
    /// </summary>
    [TestMethod]
    public async Task GetPagedAsync_And_ApplyBatchAsync_LowercaseBatchId_StillMatchesUppercaseStoredValue()
    {
        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([BuildQuote("d1111111-1111-4111-8111-111111111111", character: null)], batchId, DuplicateResolutionPolicy.NewestWins);
        var lowercaseBatchId = batchId.ToString("D");

        var page = await _service.GetPagedAsync(lowercaseBatchId, null, null, 1, 50);
        Assert.AreEqual(2, page.TotalMatching, "Quote + Source actions for a brand-new quote with no character");

        var result = await _service.ApplyBatchAsync(lowercaseBatchId);

        Assert.IsNull(result, "Nothing pending — the whole batch must apply despite the lowercase batch id");
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes"));
    }

    [TestMethod]
    public async Task ApplyBatchAsync_SomethingPending_ReturnsPendingIdsAndWritesNothing()
    {
        var id = "51111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(id, "Original text");

        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([BuildQuote(id)], batchId, DuplicateResolutionPolicy.Review);

        var result = await _service.ApplyBatchAsync(batchId.ToString("D").ToUpperInvariant());

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result!.PendingActionIds.Count);
    }

    [TestMethod]
    public async Task ApplyBatchAsync_TwoBatchesReferencingSameNewSource_IdempotentNoDuplicateSourceRow()
    {
        var batch1 = Guid.NewGuid();
        var batch2 = Guid.NewGuid();
        await PlanAndStageAsync([BuildQuote("61111111-1111-4111-8111-111111111111", character: "Rick Blaine")], batch1, DuplicateResolutionPolicy.NewestWins);
        await PlanAndStageAsync([BuildQuote("71111111-1111-4111-8111-111111111111", character: "Ilsa Lund")], batch2, DuplicateResolutionPolicy.NewestWins);

        await _service.ApplyBatchAsync(batch1.ToString("D").ToUpperInvariant());
        await _service.ApplyBatchAsync(batch2.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sources"), "Both batches staged an Add for the same Source — only one row must land");
        Assert.AreEqual(2, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Characters"), "Different characters — both must land");
        Assert.AreEqual(2, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes"));
    }

    /// <summary>
    /// #59 — before undo existed, nothing in the codebase ever soft-deleted a Quotes/Sources/
    /// Characters/People row. Every existence check the planner relies on for duplicate detection
    /// filters <c>IsDeleted = 0</c>, and all four insert statements are <c>INSERT OR IGNORE</c> — so
    /// once a row is soft-deleted, re-importing the same content would compute the same deterministic
    /// id, find nothing (soft-deleted rows are invisible), stage a fresh Add, and have that Add
    /// silently no-op against the still-occupied primary key. This test proves the fix (hard-delete
    /// the stale soft-deleted row immediately before the insert) actually resurrects the row instead.
    /// </summary>
    [TestMethod]
    public async Task ApplyResolvedActionAsync_ReAddAfterSoftDelete_ResurrectsSoftDeletedRow()
    {
        var quoteId = "81111111-1111-4111-8111-111111111111";
        var quote   = BuildQuote(quoteId);
        var batch1  = Guid.NewGuid();
        await PlanAndStageAsync([quote], batch1, DuplicateResolutionPolicy.NewestWins);
        await _service.ApplyBatchAsync(batch1.ToString("D").ToUpperInvariant());

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            var sourceId    = await conn.ExecuteScalarAsync<Guid>("SELECT SourceId FROM Quotes WHERE Id = @id", new { id = quoteId });
            var characterId = await conn.ExecuteScalarAsync<Guid>("SELECT CharacterId FROM Quotes WHERE Id = @id", new { id = quoteId });
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await conn.ExecuteAsync("UPDATE Quotes SET IsDeleted = 1, DateDeleted = @now WHERE Id = @id", new { id = quoteId, now });
            await conn.ExecuteAsync("UPDATE Sources SET IsDeleted = 1, DateDeleted = @now WHERE Id = @id", new { id = sourceId, now });
            await conn.ExecuteAsync("UPDATE Characters SET IsDeleted = 1, DateDeleted = @now WHERE Id = @id", new { id = characterId, now });
        }

        var batch2 = Guid.NewGuid();
        await PlanAndStageAsync([quote], batch2, DuplicateResolutionPolicy.NewestWins);
        var result = await _service.ApplyBatchAsync(batch2.ToString("D").ToUpperInvariant());

        Assert.IsNull(result, "Nothing pending — re-adding previously soft-deleted content must apply cleanly");
        using var verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        verifyConn.Open();
        Assert.AreEqual(0, await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes WHERE Id = @id AND IsDeleted = 1", new { id = quoteId }));
        Assert.AreEqual(1, await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes WHERE Id = @id AND IsDeleted = 0", new { id = quoteId }),
            "The quote must be resurrected as a live row, not left soft-deleted with the Add silently no-op'ing");
        Assert.AreEqual(0, await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sources WHERE IsDeleted = 1"));
        Assert.AreEqual(0, await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Characters WHERE IsDeleted = 1"));
    }

    /// <summary>
    /// Found live (T2 Docker smoke test), not by the unit suite — every prior resurrection test used
    /// a genre-less quote. <c>QuoteGenres</c> carries a hard FK to <c>Quotes(Id)</c>; a Quote-Add
    /// reversal only soft-deletes the Quote row itself, leaving its genre rows physically present,
    /// which then blocked <c>ClearStaleAddTargetsAsync</c>'s hard-delete on re-import with
    /// <c>SQLite Error 19: FOREIGN KEY constraint failed</c>. Goes through the real
    /// <see cref="SqliteImportActionService.ReverseBatchAsync"/> endpoint (not a manual soft-delete),
    /// matching exactly how the live bug was produced.
    /// </summary>
    [TestMethod]
    public async Task ReverseBatchAsync_ThenReImport_QuoteWithGenres_ResurrectsWithoutForeignKeyViolation()
    {
        var quote   = BuildQuote("b2111111-1111-4111-8111-111111111111", genres: ["comedy"]);
        var batchId = await StageApplyAndMarkAppliedAsync(quote, DuplicateResolutionPolicy.NewestWins);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        var batch2 = Guid.NewGuid();
        await PlanAndStageAsync([quote], batch2, DuplicateResolutionPolicy.NewestWins);
        var result = await _service.ApplyBatchAsync(batch2.ToString("D").ToUpperInvariant());

        Assert.IsNull(result, "Re-import after reversal must apply cleanly, not throw a FOREIGN KEY constraint error");
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes WHERE Id = @id AND IsDeleted = 0", new { id = quote.Id }));
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM QuoteGenres WHERE QuoteId = @id AND Genre = 'Comedy'", new { id = quote.Id }));
    }

    [TestMethod]
    public async Task GetPagedAsync_BrandNewQuote_QuoteActionRelatesToItsSourceAndCharacterActions()
    {
        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([BuildQuote("91111111-1111-4111-8111-111111111111")], batchId, DuplicateResolutionPolicy.NewestWins);

        var page = await _service.GetPagedAsync(batchId.ToString("D").ToUpperInvariant(), null, null, 1, 50);

        var quoteItem     = page.Items.Single(i => i.EntityType == "Quote");
        var sourceItem    = page.Items.Single(i => i.EntityType == "Source");
        var characterItem = page.Items.Single(i => i.EntityType == "Character");

        CollectionAssert.AreEquivalent(new[] { sourceItem.Id, characterItem.Id }, quoteItem.RelatedActionIds.ToList());
        Assert.IsTrue(sourceItem.RelatedActionIds.Count == 0, "Source actions never relate to other actions");
    }

    [TestMethod]
    public async Task GetPagedAsync_PendingModify_ComputesAmbiguousFields()
    {
        var id = "a1111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(id, "Original text");

        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([BuildQuote(id)], batchId, DuplicateResolutionPolicy.Review);

        var page = await _service.GetPagedAsync(batchId.ToString("D").ToUpperInvariant(), null, null, 1, 50);
        var quoteItem = page.Items.Single(i => i.EntityType == "Quote");

        Assert.AreEqual("Pending", quoteItem.Status);
        CollectionAssert.Contains(quoteItem.AmbiguousFields.ToList(), "quoteText");
    }

    [TestMethod]
    public async Task GetPagedAsync_DecidedAction_AmbiguousFieldsIsEmpty()
    {
        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([BuildQuote("b1111111-1111-4111-8111-111111111111")], batchId, DuplicateResolutionPolicy.NewestWins);

        var page = await _service.GetPagedAsync(batchId.ToString("D").ToUpperInvariant(), null, null, 1, 50);
        var quoteItem = page.Items.Single(i => i.EntityType == "Quote");

        Assert.AreEqual(0, quoteItem.AmbiguousFields.Count, "A Decided Add is never ambiguous");
    }

    [TestMethod]
    public async Task GetPagedAsync_FilterByEntityType_ReturnsOnlyMatchingRows()
    {
        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([BuildQuote("c1111111-1111-4111-8111-111111111111")], batchId, DuplicateResolutionPolicy.NewestWins);

        var page = await _service.GetPagedAsync(batchId.ToString("D").ToUpperInvariant(), null, "Source", 1, 50);

        Assert.IsTrue(page.Items.Count > 0);
        Assert.IsTrue(page.Items.All(i => i.EntityType == "Source"));
    }

    [TestMethod]
    public async Task GetPagedAsync_FilterByEntityTypeLowercase_StillMatchesUppercaseStoredValue()
    {
        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([BuildQuote("e1111111-1111-4111-8111-111111111111")], batchId, DuplicateResolutionPolicy.NewestWins);

        var page = await _service.GetPagedAsync(batchId.ToString("D").ToUpperInvariant(), null, "source", 1, 50);

        Assert.IsTrue(page.Items.Count > 0);
        Assert.IsTrue(page.Items.All(i => i.EntityType == "Source"));
    }

    [TestMethod]
    public async Task GetPagedAsync_FilterByStatusLowercase_StillMatchesStoredValue()
    {
        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([BuildQuote("f1111111-1111-4111-8111-111111111111")], batchId, DuplicateResolutionPolicy.NewestWins);

        var page = await _service.GetPagedAsync(batchId.ToString("D").ToUpperInvariant(), "decided", null, 1, 50);

        Assert.IsTrue(page.Items.Count > 0);
        Assert.IsTrue(page.Items.All(i => i.Status == "Decided"));
    }

    [TestMethod]
    public async Task DiscardBatchAsync_MarksActionsDiscarded_WritesNoDomainRows()
    {
        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([BuildQuote("81111111-1111-4111-8111-111111111111")], batchId, DuplicateResolutionPolicy.NewestWins);

        await _service.DiscardBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes"));
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sources"));

        var actions = await _actionReader.GetAllForBatchAsync(batchId.ToString("D").ToUpperInvariant());
        Assert.IsTrue(actions.All(a => a.Status.Parsed == ImportActionStatus.Discarded));
    }

    private async Task SeedExistingQuoteAsync(string id, string quoteText)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var now      = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var sourceId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO Sources (Id, Title, Type, DateCreated) VALUES (@Id, 'Casablanca', 'Movie', @now)", new { Id = sourceId, now });
        await conn.ExecuteAsync(
            "INSERT INTO Quotes (Id, QuoteText, OriginalLanguage, SourceId, DateCreated) VALUES (@Id, @quoteText, 'en', @SourceId, @now)",
            new { Id = id, quoteText, SourceId = sourceId, now });
    }

    // ── #59 — ReverseBatchAsync ──────────────────────────────────────────────

    private async Task MarkImportBatchAppliedAsync(Guid batchId)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync("UPDATE ImportBatches SET Status = 'Applied', AppliedAt = @now WHERE Id = @id", new { id = batchId, now });
    }

    private async Task<Guid> StageApplyAndMarkAppliedAsync(SourceQuote quote, DuplicateResolutionPolicy policy)
    {
        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([quote], batchId, policy);
        await _service.ApplyBatchAsync(batchId.ToString("D").ToUpperInvariant());
        await MarkImportBatchAppliedAsync(batchId);
        return batchId;
    }

    /// <summary>#68: stages+applies+marks-applied a Quote, one StageDirection, and a Conversation whose only line is that StageDirection, in one batch.</summary>
    private async Task<Guid> StageApplyAndMarkAppliedConversationAsync(string quoteId, string stageDirectionId, string conversationId)
    {
        var batchId = Guid.NewGuid();
        var quote = BuildQuote(quoteId);
        var stageDirection = new SourceStageDirection { Id = stageDirectionId, Text = "[A stage direction]" };
        var conversation = new SourceConversation
        {
            Id = conversationId,
            Lines =
            [
                new SourceConversationLine { Order = 1, Type = ConversationLineType.StageDirection, StageDirectionId = stageDirectionId },
                new SourceConversationLine { Order = 2, Type = ConversationLineType.Quote, QuoteId = quoteId },
            ],
        };
        await PlanAndStageAsync([quote], batchId, DuplicateResolutionPolicy.NewestWins, stageDirections: [stageDirection], conversations: [conversation]);
        await _service.ApplyBatchAsync(batchId.ToString("D").ToUpperInvariant());
        await MarkImportBatchAppliedAsync(batchId);
        return batchId;
    }

    /// <summary>
    /// #68: reversing a batch that introduced a Conversation and the StageDirection it references
    /// soft-deletes both — Conversation reverses first (order tier 0, alongside Quote), so
    /// StageDirection's active-reference check (joined through Conversations) correctly finds no
    /// live references left by the time it runs (order tier 4).
    /// </summary>
    [TestMethod]
    public async Task ReverseBatchAsync_ConversationAdd_SoftDeletesConversationAndOrphanedStageDirection()
    {
        var quoteId          = "d1111111-1111-4111-8111-111111111111";
        var stageDirectionId = "d2222222-2222-4222-8222-222222222222";
        var conversationId   = "d3333333-3333-4333-8333-333333333333";
        var batchId = await StageApplyAndMarkAppliedConversationAsync(quoteId, stageDirectionId, conversationId);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Conversations WHERE Id = @id AND IsDeleted = 1", new { id = conversationId }));
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM StageDirections WHERE Id = @id AND IsDeleted = 1", new { id = stageDirectionId }));
    }

    /// <summary>
    /// A StageDirection reused (not re-Added — Add-detection is by id, so the second batch's own
    /// action list never contains a StageDirection entry for an id that already exists) by a second
    /// conversation must survive reversal of that second batch — reversal only ever touches entities
    /// its own batch actually introduced, never one it merely referenced. Complements
    /// <see cref="ReverseBatchAsync_ConversationAdd_SoftDeletesConversationAndOrphanedStageDirection"/>,
    /// which is what actually exercises <c>Sql.StageDirections.CountActiveReferences</c>' join-through-
    /// Conversations logic (both rows introduced by the same reversed batch).
    /// </summary>
    [TestMethod]
    public async Task ReverseBatchAsync_StageDirectionStillReferencedByAnotherConversation_IsKeptNotSoftDeleted()
    {
        var sharedStageDirectionId = "d4444444-4444-4444-8444-444444444444";
        var quote1Id = "d5555555-5555-4555-8555-555555555555";
        var quote2Id = "d6666666-6666-4666-8666-666666666666";
        var conversation1Id = "d7777777-7777-4777-8777-777777777777";
        var conversation2Id = "d8888888-8888-4888-8888-888888888888";

        await StageApplyAndMarkAppliedConversationAsync(quote1Id, sharedStageDirectionId, conversation1Id);

        // A second batch reuses the same StageDirection id in its own conversation — Add-detection by
        // id means only the Conversation and Quote are genuinely new here; the StageDirection Add is
        // skipped (already exists), matching how re-seeding avoids duplicating a reused stage direction.
        var newerBatchId = Guid.NewGuid();
        var quote2 = BuildQuote(quote2Id);
        var conversation2 = new SourceConversation
        {
            Id = conversation2Id,
            Lines =
            [
                new SourceConversationLine { Order = 1, Type = ConversationLineType.StageDirection, StageDirectionId = sharedStageDirectionId },
                new SourceConversationLine { Order = 2, Type = ConversationLineType.Quote, QuoteId = quote2Id },
            ],
        };
        await PlanAndStageAsync([quote2], newerBatchId, DuplicateResolutionPolicy.NewestWins,
            stageDirections: [new SourceStageDirection { Id = sharedStageDirectionId, Text = "[A stage direction]" }],
            conversations: [conversation2]);
        await _service.ApplyBatchAsync(newerBatchId.ToString("D").ToUpperInvariant());
        await MarkImportBatchAppliedAsync(newerBatchId);

        await _service.ReverseBatchAsync(newerBatchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Conversations WHERE Id = @id AND IsDeleted = 1", new { id = conversation2Id }), "Newer conversation is reversed");
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM StageDirections WHERE Id = @id AND IsDeleted = 1", new { id = sharedStageDirectionId }), "StageDirection must survive — still referenced by the older, non-reversed conversation");
    }

    [TestMethod]
    public async Task ReverseBatchAsync_QuoteAdd_SoftDeletesQuote()
    {
        var id = "a1111111-1111-4111-8111-111111111111";
        var batchId = await StageApplyAndMarkAppliedAsync(BuildQuote(id), DuplicateResolutionPolicy.NewestWins);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes WHERE Id = @id AND IsDeleted = 1", new { id }));
    }

    [TestMethod]
    public async Task ReverseBatchAsync_QuoteAdd_SoftDeletesOrphanedSourceAndCharacter()
    {
        var id = "a2111111-1111-4111-8111-111111111111";
        var batchId = await StageApplyAndMarkAppliedAsync(BuildQuote(id), DuplicateResolutionPolicy.NewestWins);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sources WHERE IsDeleted = 1"));
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Characters WHERE IsDeleted = 1"));
    }

    [TestMethod]
    public async Task ReverseBatchAsync_SourceStillReferencedByAnotherBatch_IsKeptNotSoftDeleted()
    {
        // Mirrors ApplyBatchAsync_TwoBatchesReferencingSameNewSource_IdempotentNoDuplicateSourceRow's
        // setup — two batches concurrently stage an Add for the same not-yet-existing Source; only
        // reversing the newer (top-of-stack) one must leave the Source alone, since the older batch's
        // own quote is still an active reference.
        var olderId = "a3111111-1111-4111-8111-111111111111";
        var newerId = "a4111111-1111-4111-8111-111111111111";
        var olderBatch = Guid.NewGuid();
        var newerBatch = Guid.NewGuid();
        await PlanAndStageAsync([BuildQuote(olderId, character: "Rick Blaine")], olderBatch, DuplicateResolutionPolicy.NewestWins);
        await PlanAndStageAsync([BuildQuote(newerId, character: "Ilsa Lund")], newerBatch, DuplicateResolutionPolicy.NewestWins);
        await _service.ApplyBatchAsync(olderBatch.ToString("D").ToUpperInvariant());
        await MarkImportBatchAppliedAsync(olderBatch);
        await _service.ApplyBatchAsync(newerBatch.ToString("D").ToUpperInvariant());
        await MarkImportBatchAppliedAsync(newerBatch);

        await _service.ReverseBatchAsync(newerBatch.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sources WHERE IsDeleted = 1"), "The older batch's quote still actively references this Source — it must be kept");
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes WHERE Id = @id AND IsDeleted = 1", new { id = newerId }));
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes WHERE Id = @id AND IsDeleted = 0", new { id = olderId }));
    }

    [TestMethod]
    public async Task ReverseBatchAsync_QuoteModify_RestoresExistingFields()
    {
        var id = "a5111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(id, "Original text");

        var batchId = await StageApplyAndMarkAppliedAsync(BuildQuote(id, character: null, quoteText: "Modified text"), DuplicateResolutionPolicy.NewestWins);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var text = await conn.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotes WHERE Id = @id", new { id });
        Assert.AreEqual("Original text", text);
    }

    [TestMethod]
    public async Task ReverseBatchAsync_QuoteModify_SourceTextChanged_RestoresOriginalLinkage()
    {
        var id = "a6111111-1111-4111-8111-111111111111";
        Guid originalSourceId;
        await SeedExistingQuoteAsync(id, "Casablanca line");
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            originalSourceId = await conn.ExecuteScalarAsync<Guid>("SELECT SourceId FROM Quotes WHERE Id = @id", new { id });
        }

        var batchId = await StageApplyAndMarkAppliedAsync(
            BuildQuote(id, source: "A Different Movie", character: null, quoteText: "Casablanca line"),
            DuplicateResolutionPolicy.NewestWins);

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            var changedSourceId = await conn.ExecuteScalarAsync<Guid>("SELECT SourceId FROM Quotes WHERE Id = @id", new { id });
            Assert.AreNotEqual(originalSourceId, changedSourceId, "Sanity check: the Modify must actually have changed the Source linkage before reversal can prove anything");
        }

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        verifyConn.Open();
        var restoredSourceId = await verifyConn.ExecuteScalarAsync<Guid>("SELECT SourceId FROM Quotes WHERE Id = @id", new { id });
        Assert.AreEqual(originalSourceId, restoredSourceId, "Reversal must restore the original Source linkage, not the incoming quote's resolved SourceId stored on the action");
    }

    /// <summary>
    /// Under #59's own invariants (strict LIFO stack + bottom-up ordering) this specific failure is
    /// not reachable through the ordinary API surface — reversing a batch that modified quote X's
    /// Source can only happen after any later batch has already been reversed, at which point quote X
    /// (and its original Source reference) would already be restored. This test proves the defensive
    /// check itself works correctly regardless — by directly corrupting the database state after
    /// staging (deleting the original Source outright), not by trying to orchestrate the scenario
    /// through the normal flow.
    /// </summary>
    [TestMethod]
    public async Task ReverseBatchAsync_QuoteModify_OriginalSourceNoLongerExists_ThrowsImportBatchStateException()
    {
        var id = "b3111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(id, "Casablanca line");

        var batchId = await StageApplyAndMarkAppliedAsync(
            BuildQuote(id, source: "A Different Movie", character: null, quoteText: "Casablanca line"),
            DuplicateResolutionPolicy.NewestWins);

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await conn.ExecuteAsync("DELETE FROM Sources WHERE Title = 'Casablanca'");
        }

        await Assert.ThrowsExactlyAsync<ImportBatchStateException>(
            () => _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant()));
    }

    [TestMethod]
    public async Task ReverseBatchAsync_QuoteModify_RestoresExistingBatchId()
    {
        var id = "a7111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(id, "Original text");
        using var setupConn = new SqliteConnection($"Data Source={_dbPath}");
        setupConn.Open();
        var originalBatchId = Guid.NewGuid();
        await setupConn.ExecuteAsync(
            "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, DateCreated, Status) VALUES (@Id, 'original', 'Import', @now, @now, 'Applied')",
            new { Id = originalBatchId, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
        await setupConn.ExecuteAsync("UPDATE Quotes SET ImportBatchId = @batchId WHERE Id = @id", new { batchId = originalBatchId, id });

        var modifyBatchId = await StageApplyAndMarkAppliedAsync(BuildQuote(id, character: null, quoteText: "Modified text"), DuplicateResolutionPolicy.NewestWins);

        await _service.ReverseBatchAsync(modifyBatchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var restoredBatchId = await conn.ExecuteScalarAsync<Guid>("SELECT ImportBatchId FROM Quotes WHERE Id = @id", new { id });
        Assert.AreEqual(originalBatchId, restoredBatchId, "Reversal must restore ImportBatchId to the batch that actually owns the restored content, not the batch being reversed");
    }

    [TestMethod]
    public async Task ReverseBatchAsync_QuoteModify_PreservesCompletenessFlags()
    {
        var id = "a8111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(id, "Original text");
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await conn.ExecuteAsync("UPDATE Quotes SET IsComplete = 1 WHERE Id = @id", new { id });
        }

        var batchId = await StageApplyAndMarkAppliedAsync(BuildQuote(id, character: null, quoteText: "Modified text"), DuplicateResolutionPolicy.NewestWins);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        verifyConn.Open();
        var isComplete = await verifyConn.ExecuteScalarAsync<bool>("SELECT IsComplete FROM Quotes WHERE Id = @id", new { id });
        Assert.IsTrue(isComplete, "Reversal must never reset IsComplete/NoValueKnown — ExistingValue's snapshot never captured them");
    }

    [TestMethod]
    public async Task ReverseBatchAsync_SkipPolicyModify_NoWriteButReversesCleanly()
    {
        var id = "a9111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(id, "Original text");

        var batchId = await StageApplyAndMarkAppliedAsync(BuildQuote(id, character: null, quoteText: "Would-be modified text"), DuplicateResolutionPolicy.Skip);

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            var text = await conn.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotes WHERE Id = @id", new { id });
            Assert.AreEqual("Original text", text, "Sanity check: Skip must never have written anything");
        }

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        verifyConn.Open();
        var textAfter = await verifyConn.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotes WHERE Id = @id AND IsDeleted = 0", new { id });
        Assert.AreEqual("Original text", textAfter, "Reversing a Skip-policy Modify is a no-op write, not an error");
    }

    [TestMethod]
    public async Task ReverseBatchAsync_NotApplied_ThrowsImportBatchStateException()
    {
        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([BuildQuote("aa111111-1111-4111-8111-111111111111")], batchId, DuplicateResolutionPolicy.NewestWins);
        // Never applied — ImportBatches.Status stays whatever PlanAndStageAsync's raw insert left it as, never 'Applied'.

        await Assert.ThrowsExactlyAsync<ImportBatchStateException>(
            () => _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant()));
    }

    [TestMethod]
    public async Task ReverseBatchAsync_UnknownBatchId_ThrowsImportBatchNotFoundException()
        => await Assert.ThrowsExactlyAsync<ImportBatchNotFoundException>(
            () => _service.ReverseBatchAsync(Guid.NewGuid().ToString("D")));

    [TestMethod]
    public async Task ReverseBatchAsync_MalformedBatchId_ThrowsImportBatchNotFoundException()
        => await Assert.ThrowsExactlyAsync<ImportBatchNotFoundException>(
            () => _service.ReverseBatchAsync("not-a-guid"));

    [TestMethod]
    public async Task ReverseBatchAsync_AlreadyReversed_ThrowsImportBatchNotFoundException()
    {
        var batchId = await StageApplyAndMarkAppliedAsync(BuildQuote("ab111111-1111-4111-8111-111111111111"), DuplicateResolutionPolicy.NewestWins);
        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        await Assert.ThrowsExactlyAsync<ImportBatchNotFoundException>(
            () => _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant()));
    }

    [TestMethod]
    public async Task ReverseBatchAsync_NotTopOfStack_ThrowsImportBatchStateException()
    {
        var olderBatch = await StageApplyAndMarkAppliedAsync(BuildQuote("ac111111-1111-4111-8111-111111111111", character: "Rick Blaine"), DuplicateResolutionPolicy.NewestWins);
        await StageApplyAndMarkAppliedAsync(BuildQuote("ad111111-1111-4111-8111-111111111111", character: "Ilsa Lund"), DuplicateResolutionPolicy.NewestWins);

        var ex = await Assert.ThrowsExactlyAsync<ImportBatchStateException>(
            () => _service.ReverseBatchAsync(olderBatch.ToString("D").ToUpperInvariant()));
        StringAssert.Contains(ex.Message, "most recently applied");
    }

    [TestMethod]
    public async Task ReverseBatchAsync_TopOfStack_ThenNextOldest_BothSucceedInOrder()
    {
        var olderBatch = await StageApplyAndMarkAppliedAsync(BuildQuote("ae111111-1111-4111-8111-111111111111", character: "Rick Blaine"), DuplicateResolutionPolicy.NewestWins);
        var newerBatch = await StageApplyAndMarkAppliedAsync(BuildQuote("af111111-1111-4111-8111-111111111111", character: "Ilsa Lund"), DuplicateResolutionPolicy.NewestWins);

        await _service.ReverseBatchAsync(newerBatch.ToString("D").ToUpperInvariant());
        await _service.ReverseBatchAsync(olderBatch.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes WHERE IsDeleted = 0"));
    }

    [TestMethod]
    public async Task ReverseBatchAsync_ImportBatchIsSoftDeleted_ActionsRemainApplied()
    {
        var id = "b0111111-1111-4111-8111-111111111111";
        var batchId = await StageApplyAndMarkAppliedAsync(BuildQuote(id), DuplicateResolutionPolicy.NewestWins);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ImportBatches WHERE Id = @id AND IsDeleted = 1", new { id = batchId }));

        var actions = await _actionReader.GetAllForBatchAsync(batchId.ToString("D").ToUpperInvariant());
        Assert.IsTrue(actions.Count > 0);
        Assert.IsTrue(actions.All(a => a.Status.Parsed == ImportActionStatus.Applied), "SystemImportAction rows stay Applied permanently — no Reversed status is introduced");
    }

    [TestMethod]
    public async Task ReverseBatchAsync_WritesSystemChangeLogEntries()
    {
        var batchId = await StageApplyAndMarkAppliedAsync(BuildQuote("b1111111-1111-4111-8111-111111111111"), DuplicateResolutionPolicy.NewestWins);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var rows = (await conn.QueryAsync<(string EntityType, string Action)>(
            "SELECT EntityType, Action FROM System_ChangeLog")).ToList();
        Console.WriteLine(string.Join("; ", rows.Select(r => $"{r.EntityType}:{r.Action}")));
        var sourceDeleted = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sources WHERE IsDeleted = 1");
        Console.WriteLine($"Sources soft-deleted: {sourceDeleted}");

        Assert.IsTrue(rows.Any(r => r.EntityType == "quote" && r.Action == "SoftDelete"));
        Assert.IsTrue(rows.Any(r => r.EntityType == "source" && r.Action == "SoftDelete"));
    }
}
