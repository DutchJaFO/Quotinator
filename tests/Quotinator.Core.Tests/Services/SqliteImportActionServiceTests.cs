using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Helpers;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Services;

namespace Quotinator.Core.Tests.Services;

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
        IReadOnlyList<SourceEntry>? sources = null,
        IReadOnlyList<SourceStageDirection>? stageDirections = null,
        IReadOnlyList<SourceSoundCue>? soundCues = null,
        IReadOnlyList<SourceConversation>? conversations = null,
        IReadOnlyList<PersonEntry>? people = null,
        IReadOnlyList<SeriesEntry>? series = null,
        IReadOnlyList<UniverseEntry>? universe = null)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        // Sources.ImportBatchId (and Characters/People/Quotes) is a real FK to ImportBatches — the
        // applier's writes need a genuine row to reference, same as production's own batch-first flow.
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, DateCreated) VALUES (@Id, 'test', 'Import', @now, @now)",
            new { Id = batchId, now });

        var actions = await ImportActionPlanner.PlanAsync(conn, quotes, batchId, policy, sources: sources, stageDirections: stageDirections, soundCues: soundCues, conversations: conversations, people: people, series: series, universe: universe);
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
    public async Task ApplyBatchAsync_MarkCompletenessAsProvided_OverridesAutoCompute()
    {
        var id = "31211111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(id, "Original text");

        var actions = await PlanAndStageAsync([BuildQuote(id)], Guid.NewGuid(), DuplicateResolutionPolicy.Review);
        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        var batchId = quoteAction.BatchId;

        await _service.DecideAsync(quoteAction.Id, new ConflictDecisionRequest
        {
            QuoteText = new FieldDecision { Choice = FieldResolutionChoice.Replace },
            MarkCompletenessAs = CompletenessStatus.Complete,
        });
        await _service.ApplyBatchAsync(batchId);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var completenessStatus = await conn.ExecuteScalarAsync<string>("SELECT CompletenessStatus FROM Quotes WHERE Id = @id", new { id });
        Assert.AreEqual("Complete", completenessStatus, "The decide-time override must win at apply, regardless of the row's prior status");
    }

    [TestMethod]
    public async Task ApplyBatchAsync_QuoteAlreadyComplete_MarkCompletenessAsOmitted_StatusUnchanged()
    {
        var id = "31311111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(id, "Original text");
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await conn.ExecuteAsync("UPDATE Quotes SET CompletenessStatus = 'Complete' WHERE Id = @id", new { id });
        }

        var actions = await PlanAndStageAsync([BuildQuote(id)], Guid.NewGuid(), DuplicateResolutionPolicy.Review);
        var quoteAction = actions.Single(a => a.EntityType == "Quote");
        var batchId = quoteAction.BatchId;

        // #168: a changed field on a Complete row must stage Blocked, not Pending — even under
        // Review, which would otherwise have staged Pending regardless of completeness.
        Assert.AreEqual(ImportActionStatus.Blocked, quoteAction.Status.Parsed, "A Complete quote's changed field must block, not silently stage as Pending/Modify");

        using (var preDecideConn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            preDecideConn.Open();
            var textBeforeDecide = await preDecideConn.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotes WHERE Id = @id", new { id });
            Assert.AreEqual("Original text", textBeforeDecide, "The overwrite itself must not have happened yet — only staged, not applied");
        }

        await _service.DecideAsync(quoteAction.Id, new ConflictDecisionRequest
        {
            QuoteText = new FieldDecision { Choice = FieldResolutionChoice.Replace },
        });
        await _service.ApplyBatchAsync(batchId);

        using var verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        verifyConn.Open();
        var completenessStatus = await verifyConn.ExecuteScalarAsync<string>("SELECT CompletenessStatus FROM Quotes WHERE Id = @id", new { id });
        Assert.AreEqual("Complete", completenessStatus, "Omitting the override must never reset an already-Complete row back to Incomplete/NeedsReview");
        var textAfterApply = await verifyConn.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotes WHERE Id = @id", new { id });
        Assert.AreEqual("Here's looking at you, kid.", textAfterApply, "An explicit human decision on the Blocked action must still apply once made");
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
        Assert.AreEqual(2, page.TotalCount, "Quote + Source actions for a brand-new quote with no character");

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
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes WHERE UPPER(Id) = UPPER(@id) AND IsDeleted = 0", new { id = quote.Id }));
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM QuoteGenres WHERE UPPER(QuoteId) = UPPER(@id) AND Genre = 'Comedy'", new { id = quote.Id }));
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
        // #209: the applied rows are now stored under their canonicalized (uppercase) id, not the
        // lowercase id this test declared — UPPER() makes the verification query tolerant of either.
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Conversations WHERE UPPER(Id) = UPPER(@id) AND IsDeleted = 1", new { id = conversationId }));
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM StageDirections WHERE UPPER(Id) = UPPER(@id) AND IsDeleted = 1", new { id = stageDirectionId }));
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
        // #209: applied rows are stored under their canonicalized (uppercase) id — UPPER() makes the
        // verification query tolerant of the lowercase id this test declared.
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Conversations WHERE UPPER(Id) = UPPER(@id) AND IsDeleted = 1", new { id = conversation2Id }), "Newer conversation is reversed");
        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM StageDirections WHERE UPPER(Id) = UPPER(@id) AND IsDeleted = 1", new { id = sharedStageDirectionId }), "StageDirection must survive — still referenced by the older, non-reversed conversation");
    }

    [TestMethod]
    public async Task ReverseBatchAsync_QuoteAdd_SoftDeletesQuote()
    {
        var id = "a1111111-1111-4111-8111-111111111111";
        var batchId = await StageApplyAndMarkAppliedAsync(BuildQuote(id), DuplicateResolutionPolicy.NewestWins);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        // #210: applied rows are stored under their canonicalized (uppercase) id — UPPER() makes the
        // verification query tolerant of the lowercase id this test declared.
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes WHERE UPPER(Id) = UPPER(@id) AND IsDeleted = 1", new { id }));
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
        // #210: applied rows are stored under their canonicalized (uppercase) id — UPPER() makes the
        // verification query tolerant of the lowercase id this test declared.
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes WHERE UPPER(Id) = UPPER(@id) AND IsDeleted = 1", new { id = newerId }));
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes WHERE UPPER(Id) = UPPER(@id) AND IsDeleted = 0", new { id = olderId }));
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

    /// <summary>
    /// #168: a `Complete` row's Modify can no longer reach Applied at all (it stages `Blocked`
    /// instead) — so this test now uses `NeedsReview`, the highest completeness status a genuine
    /// Modify can still reach. The scenario it protects (reversal must not clobber completeness
    /// flags via the `ExistingValue` snapshot restore) is unaffected by which non-`Complete` status is used.
    /// </summary>
    [TestMethod]
    public async Task ReverseBatchAsync_QuoteModify_PreservesCompletenessFlags()
    {
        var id = "a8111111-1111-4111-8111-111111111111";
        await SeedExistingQuoteAsync(id, "Original text");
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await conn.ExecuteAsync("UPDATE Quotes SET CompletenessStatus = 'NeedsReview' WHERE Id = @id", new { id });
        }

        var batchId = await StageApplyAndMarkAppliedAsync(BuildQuote(id, character: null, quoteText: "Modified text"), DuplicateResolutionPolicy.NewestWins);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        verifyConn.Open();
        var completenessStatus = await verifyConn.ExecuteScalarAsync<string>("SELECT CompletenessStatus FROM Quotes WHERE Id = @id", new { id });
        Assert.AreEqual("NeedsReview", completenessStatus, "Reversal must never reset CompletenessStatus/NoValueKnown — ExistingValue's snapshot never captured them");
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

    // ── #162 — Source decidability ───────────────────────────────────────────

    private async Task SeedExplicitSourceAsync(string id, string title = "Casablanca", string type = "Movie", string? date = "1942")
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Sources (Id, Title, Type, Date, DateCreated) VALUES (@Id, @Title, @Type, @Date, @now)",
            new { Id = id, Title = title, Type = type, Date = date, now });
    }

    [TestMethod]
    public async Task DecideImportAction_SourceEntityType_AcceptsTitleTypeDateDecisions()
    {
        var id = "c8111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(id, title: "Casablanca");

        var actions = await PlanAndStageAsync([], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            sources: [new SourceEntry { Id = id, Title = "Casablanca (1942)", Type = QuoteType.Movie, Date = "1942-11-26" }]);
        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Pending, sourceAction.Status.Parsed, "Review policy leaves the Modify pending");

        await _service.DecideAsync(sourceAction.Id, new ConflictDecisionRequest
        {
            SourceTitle = new FieldDecision { Choice = FieldResolutionChoice.Replace },
            SourceType  = new FieldDecision { Choice = FieldResolutionChoice.Keep },
            SourceDate  = new FieldDecision { Choice = FieldResolutionChoice.Replace },
        });

        var found = await _actionReader.GetByIdAsync(sourceAction.Id);
        Assert.AreEqual(ImportActionStatus.Decided, found!.Status.Parsed);
        Assert.IsNotNull(found.MergedFields);
    }

    [TestMethod]
    public async Task ApplyBatchAsync_SourceModify_WritesResolvedFields()
    {
        var id = "c9111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(id, title: "Casablanca", date: "1942");

        var actions = await PlanAndStageAsync([], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [new SourceEntry { Id = id, Title = "Casablanca (1942)", Type = QuoteType.Movie, Date = "1942-11-26" }]);
        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Decided, sourceAction.Status.Parsed, "NewestWins resolves immediately");

        var result = await _service.ApplyBatchAsync(sourceAction.BatchId);
        Assert.IsNull(result);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var (title, date) = await conn.QuerySingleAsync<(string Title, string Date)>(
            "SELECT Title, Date FROM Sources WHERE Id = @id", new { id });
        Assert.AreEqual("Casablanca (1942)", title);
        Assert.AreEqual("1942-11-26", date);
    }

    [TestMethod]
    public async Task ApplyBatchAsync_SourceModifyWithAbsentDate_LeavesExistingDateIntact()
    {
        var id = "c9111111-1111-4111-8111-111111111112";
        await SeedExplicitSourceAsync(id, title: "Casablanca", date: "1942");

        // Title changes; Date is never mentioned — must survive the apply untouched (#190).
        var actions = await PlanAndStageAsync([], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [new SourceEntry { Id = id, Title = "Casablanca (1942)", Type = QuoteType.Movie }]);
        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Decided, sourceAction.Status.Parsed);

        var result = await _service.ApplyBatchAsync(sourceAction.BatchId);
        Assert.IsNull(result);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var (title, date) = await conn.QuerySingleAsync<(string Title, string Date)>(
            "SELECT Title, Date FROM Sources WHERE Id = @id", new { id });
        Assert.AreEqual("Casablanca (1942)", title);
        Assert.AreEqual("1942", date, "An omitted 'date' must never be reset — the real value must survive the apply unchanged");
    }

    [TestMethod]
    public async Task ApplyBatchAsync_SourceModifyWithExplicitNullDate_ClearsDate()
    {
        var id = "c9111111-1111-4111-8111-111111111113";
        await SeedExplicitSourceAsync(id, title: "Casablanca", date: "1942");

        var actions = await PlanAndStageAsync([], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            sources: [new SourceEntry { Id = id, Title = "Casablanca", Type = QuoteType.Movie, Date = Optional<string>.Of(null) }]);
        var sourceAction = actions.Single(a => a.EntityType == "Source");
        Assert.AreEqual(ImportActionStatus.Decided, sourceAction.Status.Parsed);

        var result = await _service.ApplyBatchAsync(sourceAction.BatchId);
        Assert.IsNull(result);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var date = await conn.QuerySingleAsync<string?>("SELECT Date FROM Sources WHERE Id = @id", new { id });
        Assert.IsNull(date, "An explicit 'date: null' must genuinely clear the stored value on apply");
    }

    /// <summary>
    /// #180 regression guard, found live via T2: <c>DecideAsync</c>'s Source branch reconstructed
    /// <see cref="SourceActionPayload"/> with only Title/Type/Date, silently dropping SeriesId to its
    /// default null even though <c>FieldMergeResolver</c> had already resolved it correctly — every
    /// decided Source SeriesId Modify applied as null. This is the one test in the whole suite that
    /// exercises the full plan → stage → decide → apply → verify-on-disk pipeline for SeriesId; the
    /// planner-level and Blocked/Pending-only tests elsewhere never actually apply the resolved value.
    /// </summary>
    [TestMethod]
    public async Task ApplyBatchAsync_SourceSeriesIdDecided_WritesResolvedSeriesId()
    {
        var id = "cb111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(id, title: "Casablanca");

        var actions = await PlanAndStageAsync([], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            sources: [new SourceEntry { Id = id, Title = "Casablanca", Type = QuoteType.Movie, SeriesName = "Test Series" }],
            series: [new SeriesEntry { Name = "Test Series" }]);
        var sourceAction = actions.Single(a => a.EntityType == "Source" && a.ActionType.Parsed == ImportActionKind.Modify);
        var seriesAction = actions.Single(a => a.EntityType == "Series");
        Assert.AreEqual(ImportActionStatus.Pending, sourceAction.Status.Parsed, "First-time SeriesId fill under Review still stages Pending");

        await _service.DecideAsync(sourceAction.Id, new ConflictDecisionRequest
        {
            SourceSeriesId = new FieldDecision { Choice = FieldResolutionChoice.Replace },
        });

        var result = await _service.ApplyBatchAsync(sourceAction.BatchId);
        Assert.IsNull(result, "Every action in the batch (the Series Add and the now-Decided Source Modify) must apply cleanly");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var storedSeriesId = await conn.QuerySingleAsync<string?>("SELECT SeriesId FROM Sources WHERE Id = @id", new { id });
        Assert.AreEqual(seriesAction.EntityId, storedSeriesId, "The resolved SeriesId must actually be written to the Sources row, not silently dropped to null");
    }

    [TestMethod]
    public async Task ReverseAppliedActionsAsync_SourceModify_RestoresExistingValue()
    {
        var id = "ca111111-1111-4111-8111-111111111111";
        await SeedExplicitSourceAsync(id, title: "Casablanca", date: "1942");

        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([], batchId, DuplicateResolutionPolicy.NewestWins,
            sources: [new SourceEntry { Id = id, Title = "Casablanca (Corrected)", Type = QuoteType.Movie, Date = "1942-11-26" }]);
        await _service.ApplyBatchAsync(batchId.ToString("D").ToUpperInvariant());
        await MarkImportBatchAppliedAsync(batchId);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var (title, date) = await conn.QuerySingleAsync<(string Title, string Date)>(
            "SELECT Title, Date FROM Sources WHERE Id = @id", new { id });
        Assert.AreEqual("Casablanca", title, "Reversal must restore the pre-Modify title");
        Assert.AreEqual("1942", date, "Reversal must restore the pre-Modify date");
    }

    [TestMethod]
    public async Task ReverseAppliedActionsAsync_SourceAdd_StillSoftDeletesIfUnreferenced()
    {
        var newFileId = "cb111111-1111-4111-8111-111111111111";
        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([], batchId, DuplicateResolutionPolicy.NewestWins,
            sources: [new SourceEntry { Id = newFileId, Title = "A Brand New Film", Type = QuoteType.Movie }]);
        await _service.ApplyBatchAsync(batchId.ToString("D").ToUpperInvariant());
        await MarkImportBatchAppliedAsync(batchId);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        // #209: the row is stored under its canonicalized (uppercase) id, not the lowercase id this
        // test declared — UPPER() makes the verification query tolerant of either.
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sources WHERE UPPER(Id) = UPPER(@id) AND IsDeleted = 1", new { id = newFileId }));
    }

    /// <summary>
    /// Regression guard, found live via T2: a lowercase, explicit file-authored Source id (from a
    /// <c>sources[]</c> entry, added in one import batch alongside a quote that references it) must
    /// resolve to the exact same row on a later, separate import batch that corrects that Source's
    /// Title — not a second, duplicate row. The bug: <c>ResolveSourceAsync</c>'s natural-key lookup
    /// read the matched id through <c>Guid?</c>-typed Dapper mapping and then force-uppercased it
    /// (<c>foundId.ToString("D").ToUpperInvariant()</c>) — safe before #162, when every Source id was
    /// either <c>Guid.NewGuid()</c>-based or <c>EntityIdentity</c>-derived, both always stored
    /// uppercase. A lowercase file-authored id broke that assumption: <c>Guid</c> has no memory of
    /// original string casing, so re-casing the natural-key match produced a string that didn't match
    /// the actual stored row, and the Quote's own defensive <c>EnsureSourceExistsAsync</c> call
    /// silently created a second Source row with the wrong-cased id.
    /// </summary>
    [TestMethod]
    public async Task ApplyBatchAsync_LowercaseExplicitSourceId_SecondBatchCorrection_NeverCreatesDuplicateRow()
    {
        var sourceId = "bbbbbbbb-2222-4222-8222-222222222222";
        var quoteId  = "cc111111-1111-4111-8111-111111111111";
        var quote    = BuildQuote(quoteId, source: "T2 Regression Movie", character: null);

        var batch1 = Guid.NewGuid();
        await PlanAndStageAsync([quote], batch1, DuplicateResolutionPolicy.NewestWins,
            sources: [new SourceEntry { Id = sourceId, Title = "T2 Regression Movie", Type = QuoteType.Movie }]);
        await _service.ApplyBatchAsync(batch1.ToString("D").ToUpperInvariant());

        var batch2 = Guid.NewGuid();
        await PlanAndStageAsync([quote], batch2, DuplicateResolutionPolicy.NewestWins,
            sources: [new SourceEntry { Id = sourceId, Title = "T2 Regression Movie (Corrected)", Type = QuoteType.Movie }]);
        await _service.ApplyBatchAsync(batch2.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sources WHERE Title LIKE 'T2 Regression Movie%'");
        Assert.AreEqual(1, count, "Must be exactly one Source row — the correction must have found and updated the original, not created a second one");

        // #209: the row is stored under its canonicalized (uppercase) id, not the lowercase id this
        // test declared — UPPER() makes the verification query tolerant of either.
        var title = await conn.ExecuteScalarAsync<string>("SELECT Title FROM Sources WHERE UPPER(Id) = UPPER(@id)", new { id = sourceId });
        Assert.AreEqual("T2 Regression Movie (Corrected)", title);
    }

    // ── #209 — canonicalize explicit ids at capture ──────────────────────────

    public static IEnumerable<object[]> ExplicitIdCapableEntityInsertCases()
    {
        const string now = "2026-01-01 00:00:00";
        return
        [
            ["Sources", (Func<SqliteConnection, string, Task>)(async (conn, id) =>
                await conn.ExecuteAsync("INSERT INTO Sources (Id, Title, Type, DateCreated) VALUES (@Id, 'Storage Guard Source', 'Movie', @now)", new { Id = id, now }))],
            ["People", (Func<SqliteConnection, string, Task>)(async (conn, id) =>
                await conn.ExecuteAsync("INSERT INTO People (Id, Name, DateCreated) VALUES (@Id, 'Storage Guard Person', @now)", new { Id = id, now }))],
            ["StageDirections", (Func<SqliteConnection, string, Task>)(async (conn, id) =>
                await conn.ExecuteAsync("INSERT INTO StageDirections (Id, Text, DateCreated) VALUES (@Id, 'Storage guard direction.', @now)", new { Id = id, now }))],
            ["SoundCues", (Func<SqliteConnection, string, Task>)(async (conn, id) =>
                await conn.ExecuteAsync("INSERT INTO SoundCues (Id, Text, DateCreated) VALUES (@Id, 'Storage guard cue.', @now)", new { Id = id, now }))],
            ["Conversations", (Func<SqliteConnection, string, Task>)(async (conn, id) =>
                await conn.ExecuteAsync("INSERT INTO Conversations (Id, Description, DateCreated) VALUES (@Id, 'Storage guard conversation.', @now)", new { Id = id, now }))],
        ];
    }

    /// <summary>
    /// Storage-layer invariant guard (ADR 012) — canonicalize an uppercase-cased raw id via
    /// <see cref="EntityIdCanonicalizer"/>, insert it directly into each explicit-id-capable table via
    /// minimal raw SQL, then assert a <see cref="Guid"/>-typed lookup (which <c>GuidHandler</c>
    /// re-renders to lowercase) still finds it. Proves the invariant a canonicalized capture-point fix
    /// relies on — a canonically-written row is always reachable via a Guid-typed lookup regardless of
    /// the lookup value's own original casing — independent of whichever entity-specific planner logic
    /// produced the write.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(ExplicitIdCapableEntityInsertCases))]
    public async Task CanonicalizedLowercaseId_InsertedDirectly_FoundByGuidTypedLookup(string tableName, Func<SqliteConnection, string, Task> insertRow)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var rawUppercaseId = Guid.NewGuid().ToString("D").ToUpperInvariant();
        var canonicalId = EntityIdCanonicalizer.CanonicalizeLowercase(rawUppercaseId);
        await insertRow(conn, canonicalId);

        var found = await conn.QuerySingleOrDefaultAsync<Guid?>($"SELECT Id FROM {tableName} WHERE Id = @id", new { id = Guid.Parse(rawUppercaseId) });
        Assert.IsNotNull(found, $"{tableName}: a canonically-written row must be findable via a Guid-typed lookup regardless of the lookup value's own original casing");
    }

    [TestMethod]
    public async Task ApplyBatchAsync_LowercaseExplicitSourceId_QuoteJoinStillResolves()
    {
        var lowercaseSourceId = "f0111111-1111-4111-8111-111111111209";
        var quoteId = "f1111111-1111-4111-8111-111111111209";
        var quote = BuildQuote(quoteId, source: "Canonicalization Join Test Film", character: null);

        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([quote], batchId, DuplicateResolutionPolicy.NewestWins,
            sources: [new SourceEntry { Id = lowercaseSourceId, Title = "Canonicalization Join Test Film", Type = QuoteType.Movie, Date = "1999" }]);
        await _service.ApplyBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var (title, date) = await conn.QuerySingleAsync<(string Title, string Date)>(
            "SELECT s.Title, s.Date FROM Quotes q JOIN Sources s ON s.Id = q.SourceId WHERE UPPER(q.Id) = UPPER(@id)", new { id = quoteId });
        Assert.AreEqual("Canonicalization Join Test Film", title, "The Quote->Source join must still resolve after the Source's explicit id was canonicalized at capture");
        Assert.AreEqual("1999", date);
    }

    [TestMethod]
    public async Task ApplyBatchAsync_LowercaseExplicitSourceId_MasterdataRepositoryLookupResolves()
    {
        var lowercaseSourceId = "f2111111-1111-4111-8111-111111111209";

        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([], batchId, DuplicateResolutionPolicy.NewestWins,
            sources: [new SourceEntry { Id = lowercaseSourceId, Title = "Canonicalization Repository Test Film", Type = QuoteType.Movie }]);
        await _service.ApplyBatchAsync(batchId.ToString("D").ToUpperInvariant());

        // The exact call the masterdata GET /sources/{id} endpoint makes (SourceEndpoints.GetById).
        var repository = new SqliteRestorableRepository<Source>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var source = await repository.GetByIdAsync(Guid.Parse(lowercaseSourceId));
        Assert.IsNotNull(source, "The masterdata repository's Guid-typed lookup must resolve a Source whose explicit id was file-authored in lowercase");
        Assert.AreEqual("Canonicalization Repository Test Film", source!.Title);
    }

    /// <summary>
    /// #209 regression guard: a Conversation's <c>lines[].stageDirectionId</c>/<c>soundCueId</c> is a
    /// curator-typed reference to another entry declared elsewhere in the same file — it is under no
    /// obligation to match that entry's own explicit id's casing exactly. Once #209 canonicalizes
    /// StageDirections.Id/SoundCues.Id to uppercase at capture, a cross-reference using a *different*
    /// casing than the declaration must still resolve, or ConversationLines' real FOREIGN KEY
    /// constraint to StageDirections(Id)/SoundCues(Id) fails outright — found live via the bundled
    /// curated file's own real seeding data during this issue's own verification pass.
    /// </summary>
    [TestMethod]
    public async Task ApplyBatchAsync_ConversationLineReferencesDifferentlyCasedStageDirectionAndSoundCue_ForeignKeyHolds()
    {
        var stageDirectionId = "f3111111-1111-4111-8111-111111111209";
        var soundCueId       = "f4111111-1111-4111-8111-111111111209";
        var conversationId   = "f5111111-1111-4111-8111-111111111209";

        var conversation = new SourceConversation
        {
            Id = conversationId,
            Lines =
            [
                // Deliberately uppercase references against lowercase-declared entries below —
                // the exact mismatch #209's own canonicalization would otherwise introduce.
                new SourceConversationLine { Order = 1, Type = ConversationLineType.StageDirection, StageDirectionId = stageDirectionId.ToUpperInvariant() },
                new SourceConversationLine { Order = 2, Type = ConversationLineType.SoundCue, SoundCueId = soundCueId.ToUpperInvariant() },
            ],
        };

        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([], batchId, DuplicateResolutionPolicy.NewestWins,
            stageDirections: [new SourceStageDirection { Id = stageDirectionId, Text = "A shot rings out." }],
            soundCues: [new SourceSoundCue { Id = soundCueId, Text = "Distant thunder." }],
            conversations: [conversation]);

        // Must not throw SQLite Error 19 (FOREIGN KEY constraint failed).
        await _service.ApplyBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var lineCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ConversationLines WHERE UPPER(ConversationId) = UPPER(@id) AND IsDeleted = 0", new { id = conversationId });
        Assert.AreEqual(2, lineCount, "Both lines must have actually been written — a caught-and-swallowed FK failure would leave this at 0, not just throw");
    }

    // ── #171 — StageDirection decidability ───────────────────────────────────

    private async Task SeedExplicitStageDirectionAsync(string id, string text = "A shot rings out.", string? imageUrl = null, string completenessStatus = "Incomplete")
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO StageDirections (Id, Text, ImageUrl, CompletenessStatus, DateCreated) VALUES (@Id, @Text, @ImageUrl, @CompletenessStatus, @now)",
            new { Id = id, Text = text, ImageUrl = imageUrl, CompletenessStatus = completenessStatus, now });
    }

    [TestMethod]
    public async Task DecideAsync_StageDirectionModify_ResolvesFieldDecisions()
    {
        var id = "e1111111-1111-4111-8111-111111111111";
        await SeedExplicitStageDirectionAsync(id, text: "A shot rings out.");

        var actions = await PlanAndStageAsync([], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            stageDirections: [new SourceStageDirection { Id = id, Text = "A single shot rings out.", ImageUrl = "https://example.com/still.jpg" }]);
        var action = actions.Single(a => a.EntityType == "StageDirection");
        Assert.AreEqual(ImportActionStatus.Pending, action.Status.Parsed, "Review policy leaves the Modify pending");

        await _service.DecideAsync(action.Id, new ConflictDecisionRequest
        {
            StageDirectionText     = new FieldDecision { Choice = FieldResolutionChoice.Replace },
            StageDirectionImageUrl = new FieldDecision { Choice = FieldResolutionChoice.Replace },
        });

        var found = await _actionReader.GetByIdAsync(action.Id);
        Assert.AreEqual(ImportActionStatus.Decided, found!.Status.Parsed);
        Assert.IsNotNull(found.MergedFields);
    }

    [TestMethod]
    public async Task ReverseBatchAsync_StageDirectionModify_RestoresExistingValue()
    {
        var id = "e2111111-1111-4111-8111-111111111111";
        await SeedExplicitStageDirectionAsync(id, text: "A shot rings out.", imageUrl: "https://example.com/original.jpg");
        using (var seedConn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await seedConn.OpenAsync();
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await seedConn.ExecuteAsync(
                "INSERT INTO StageDirectionTranslations (Id, StageDirectionId, Language, Text, DateCreated) VALUES (@Id, @StageDirectionId, 'nl', 'Er klinkt een schot.', @now)",
                new { Id = Guid.NewGuid().ToString(), StageDirectionId = id, now });
        }

        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([], batchId, DuplicateResolutionPolicy.NewestWins,
            stageDirections: [new SourceStageDirection { Id = id, Text = "A different action entirely.", ImageUrl = "https://example.com/corrected.jpg" }]);
        await _service.ApplyBatchAsync(batchId.ToString("D").ToUpperInvariant());
        await MarkImportBatchAppliedAsync(batchId);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var (text, imageUrl) = await conn.QuerySingleAsync<(string Text, string ImageUrl)>(
            "SELECT Text, ImageUrl FROM StageDirections WHERE Id = @id", new { id });
        Assert.AreEqual("A shot rings out.", text, "Reversal must restore the pre-Modify text");
        Assert.AreEqual("https://example.com/original.jpg", imageUrl, "Reversal must restore the pre-Modify image URL");

        var translationText = await conn.ExecuteScalarAsync<string>(
            "SELECT Text FROM StageDirectionTranslations WHERE StageDirectionId = @id AND Language = 'nl'", new { id });
        Assert.AreEqual("Er klinkt een schot.", translationText, "Translations are out of scope for Modify — must survive untouched");
    }

    // ── #172 — SoundCue decidability ─────────────────────────────────────────

    private async Task SeedExplicitSoundCueAsync(string id, string text = "Distant thunder.", string? soundFileUrl = null, string? imageUrl = null, string completenessStatus = "Incomplete")
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO SoundCues (Id, Text, SoundFileUrl, ImageUrl, CompletenessStatus, DateCreated) VALUES (@Id, @Text, @SoundFileUrl, @ImageUrl, @CompletenessStatus, @now)",
            new { Id = id, Text = text, SoundFileUrl = soundFileUrl, ImageUrl = imageUrl, CompletenessStatus = completenessStatus, now });
    }

    [TestMethod]
    public async Task DecideAsync_SoundCueModify_ResolvesFieldDecisions()
    {
        var id = "e3111111-1111-4111-8111-111111111111";
        await SeedExplicitSoundCueAsync(id, text: "Distant thunder.");

        var actions = await PlanAndStageAsync([], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            soundCues: [new SourceSoundCue { Id = id, Text = "Rolling thunder.", SoundFileUrl = "https://example.com/thunder.mp3", ImageUrl = "https://example.com/storm.jpg" }]);
        var action = actions.Single(a => a.EntityType == "SoundCue");
        Assert.AreEqual(ImportActionStatus.Pending, action.Status.Parsed, "Review policy leaves the Modify pending");

        await _service.DecideAsync(action.Id, new ConflictDecisionRequest
        {
            SoundCueText         = new FieldDecision { Choice = FieldResolutionChoice.Replace },
            SoundCueSoundFileUrl = new FieldDecision { Choice = FieldResolutionChoice.Replace },
            SoundCueImageUrl     = new FieldDecision { Choice = FieldResolutionChoice.Replace },
        });

        var found = await _actionReader.GetByIdAsync(action.Id);
        Assert.AreEqual(ImportActionStatus.Decided, found!.Status.Parsed);
        Assert.IsNotNull(found.MergedFields);
    }

    [TestMethod]
    public async Task ReverseBatchAsync_SoundCueModify_RestoresExistingValue()
    {
        var id = "e4111111-1111-4111-8111-111111111111";
        await SeedExplicitSoundCueAsync(id, text: "Distant thunder.", soundFileUrl: "https://example.com/original.mp3");
        using (var seedConn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await seedConn.OpenAsync();
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await seedConn.ExecuteAsync(
                "INSERT INTO SoundCueTranslations (Id, SoundCueId, Language, Text, DateCreated) VALUES (@Id, @SoundCueId, 'nl', 'Ver gerommel van de donder.', @now)",
                new { Id = Guid.NewGuid().ToString(), SoundCueId = id, now });
        }

        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([], batchId, DuplicateResolutionPolicy.NewestWins,
            soundCues: [new SourceSoundCue { Id = id, Text = "A completely different sound.", SoundFileUrl = "https://example.com/corrected.mp3" }]);
        await _service.ApplyBatchAsync(batchId.ToString("D").ToUpperInvariant());
        await MarkImportBatchAppliedAsync(batchId);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var (text, soundFileUrl) = await conn.QuerySingleAsync<(string Text, string SoundFileUrl)>(
            "SELECT Text, SoundFileUrl FROM SoundCues WHERE Id = @id", new { id });
        Assert.AreEqual("Distant thunder.", text, "Reversal must restore the pre-Modify text");
        Assert.AreEqual("https://example.com/original.mp3", soundFileUrl, "Reversal must restore the pre-Modify sound file URL");

        var translationText = await conn.ExecuteScalarAsync<string>(
            "SELECT Text FROM SoundCueTranslations WHERE SoundCueId = @id AND Language = 'nl'", new { id });
        Assert.AreEqual("Ver gerommel van de donder.", translationText, "Translations are out of scope for Modify — must survive untouched");
    }

    // ── #176 — Conversation decidability ─────────────────────────────────────

    private async Task SeedExplicitConversationAsync(string id, string? description = "A tense standoff.", string completenessStatus = "Incomplete")
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Conversations (Id, Description, CompletenessStatus, DateCreated) VALUES (@Id, @Description, @CompletenessStatus, @now)",
            new { Id = id, Description = description, CompletenessStatus = completenessStatus, now });
    }

    [TestMethod]
    public async Task DecideAsync_ConversationModify_ResolvesDescriptionDecision()
    {
        var id = "e5111111-1111-4111-8111-111111111176";
        await SeedExplicitConversationAsync(id, description: "A tense standoff.");

        var actions = await PlanAndStageAsync([], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            conversations: [new SourceConversation { Id = id, Description = "A tense standoff in the saloon.", Lines = [] }]);
        var action = actions.Single(a => a.EntityType == "Conversation");
        Assert.AreEqual(ImportActionStatus.Pending, action.Status.Parsed, "Review policy leaves the Modify pending");

        await _service.DecideAsync(action.Id, new ConflictDecisionRequest
        {
            ConversationDescription = new FieldDecision { Choice = FieldResolutionChoice.Replace },
        });

        var found = await _actionReader.GetByIdAsync(action.Id);
        Assert.AreEqual(ImportActionStatus.Decided, found!.Status.Parsed);
        Assert.IsNotNull(found.MergedFields);
    }

    [TestMethod]
    public async Task ReverseBatchAsync_ConversationModify_RestoresDescriptionOnly()
    {
        var id = "e6111111-1111-4111-8111-111111111176";
        await SeedExplicitConversationAsync(id, description: "A tense standoff.");

        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([], batchId, DuplicateResolutionPolicy.NewestWins,
            conversations: [new SourceConversation { Id = id, Description = "A completely different scene.", Lines = [] }]);
        await _service.ApplyBatchAsync(batchId.ToString("D").ToUpperInvariant());
        await MarkImportBatchAppliedAsync(batchId);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var description = await conn.ExecuteScalarAsync<string>("SELECT Description FROM Conversations WHERE Id = @id", new { id });
        Assert.AreEqual("A tense standoff.", description, "Reversal must restore the pre-Modify description");

        var lineCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ConversationLines WHERE ConversationId = @id", new { id });
        Assert.AreEqual(0, lineCount, "Lines are never touched by a Modify reversal");
    }

    // ── #173 — Person decidability + explicit id ─────────────────────────────

    private async Task SeedExplicitPersonAsync(string id, string name = "Ada Lovelace", string? dateOfBirth = "1815-12-10", string? dateOfDeath = "1852-11-27", string completenessStatus = "Incomplete")
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO People (Id, Name, DateOfBirth, DateOfDeath, CompletenessStatus, DateCreated) VALUES (@Id, @Name, @DateOfBirth, @DateOfDeath, @CompletenessStatus, @now)",
            new { Id = id, Name = name, DateOfBirth = dateOfBirth, DateOfDeath = dateOfDeath, CompletenessStatus = completenessStatus, now });
    }

    [TestMethod]
    public async Task ApplyBatchAsync_PersonModify_WritesDateOfBirthAndDateOfDeath()
    {
        var id = "e7111111-1111-4111-8111-111111111173";
        await SeedExplicitPersonAsync(id, name: "Ada Lovelace", dateOfBirth: null, dateOfDeath: null);

        var actions = await PlanAndStageAsync([], Guid.NewGuid(), DuplicateResolutionPolicy.NewestWins,
            people: [new PersonEntry { Id = id, Name = "Ada Lovelace", DateOfBirth = "1815-12-10", DateOfDeath = "1852-11-27" }]);
        var action = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(ImportActionStatus.Decided, action.Status.Parsed, "NewestWins resolves immediately");

        var result = await _service.ApplyBatchAsync(action.BatchId);
        Assert.IsNull(result);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var (dob, dod) = await conn.QuerySingleAsync<(string DateOfBirth, string DateOfDeath)>(
            "SELECT DateOfBirth, DateOfDeath FROM People WHERE Id = @id", new { id });
        Assert.AreEqual("1815-12-10", dob);
        Assert.AreEqual("1852-11-27", dod);
    }

    [TestMethod]
    public async Task DecideAsync_PersonModify_ResolvesFieldDecisions()
    {
        var id = "e8111111-1111-4111-8111-111111111173";
        await SeedExplicitPersonAsync(id, name: "Ada Lovelace");

        var actions = await PlanAndStageAsync([], Guid.NewGuid(), DuplicateResolutionPolicy.Review,
            people: [new PersonEntry { Id = id, Name = "Augusta Ada King", DateOfBirth = "1815-12-10", DateOfDeath = "1852-11-27" }]);
        var action = actions.Single(a => a.EntityType == "Person");
        Assert.AreEqual(ImportActionStatus.Pending, action.Status.Parsed, "Review policy leaves the Modify pending");

        await _service.DecideAsync(action.Id, new ConflictDecisionRequest
        {
            PersonName          = new FieldDecision { Choice = FieldResolutionChoice.Replace },
            PersonDateOfBirth   = new FieldDecision { Choice = FieldResolutionChoice.Replace },
            PersonDateOfDeath   = new FieldDecision { Choice = FieldResolutionChoice.Replace },
        });

        var found = await _actionReader.GetByIdAsync(action.Id);
        Assert.AreEqual(ImportActionStatus.Decided, found!.Status.Parsed);
        Assert.IsNotNull(found.MergedFields);
    }

    [TestMethod]
    public async Task ReverseBatchAsync_PersonModify_RestoresExistingValue()
    {
        var id = "e9111111-1111-4111-8111-111111111173";
        await SeedExplicitPersonAsync(id, name: "Ada Lovelace", dateOfBirth: "1815-12-10", dateOfDeath: "1852-11-27");

        var batchId = Guid.NewGuid();
        await PlanAndStageAsync([], batchId, DuplicateResolutionPolicy.NewestWins,
            people: [new PersonEntry { Id = id, Name = "A Completely Different Name", DateOfBirth = "1900-01-01", DateOfDeath = "1980-01-01" }]);
        await _service.ApplyBatchAsync(batchId.ToString("D").ToUpperInvariant());
        await MarkImportBatchAppliedAsync(batchId);

        await _service.ReverseBatchAsync(batchId.ToString("D").ToUpperInvariant());

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var (name, dob, dod) = await conn.QuerySingleAsync<(string Name, string DateOfBirth, string DateOfDeath)>(
            "SELECT Name, DateOfBirth, DateOfDeath FROM People WHERE Id = @id", new { id });
        Assert.AreEqual("Ada Lovelace", name, "Reversal must restore the pre-Modify name");
        Assert.AreEqual("1815-12-10", dob, "Reversal must restore the pre-Modify date of birth");
        Assert.AreEqual("1852-11-27", dod, "Reversal must restore the pre-Modify date of death");
    }

    /// <summary>
    /// Regression guard for #173's required <c>ClearStaleAddTargetsAsync</c> fix: a soft-deleted
    /// Person row at a lowercase, explicit file-authored id must actually be hard-deleted before a
    /// fresh Add re-applies at the same id — the old <c>_personRepository.HardDeleteAsync(Guid.Parse(...))</c>
    /// path force-uppercases via <c>GuidHandler</c> and silently matches zero rows against a
    /// lowercase-stored id, leaving <c>INSERT OR IGNORE</c> to no-op against the still-present stale row.
    /// </summary>
    [TestMethod]
    public async Task ClearStaleAddTargetsAsync_PersonExplicitLowercaseId_HardDeletesCorrectly()
    {
        var id = "ea111111-1111-4111-8111-111111111173"; // lowercase, explicit file-authored id

        var batch1 = Guid.NewGuid();
        await PlanAndStageAsync([], batch1, DuplicateResolutionPolicy.NewestWins,
            people: [new PersonEntry { Id = id, Name = "Original Name" }]);
        await _service.ApplyBatchAsync(batch1.ToString("D").ToUpperInvariant());
        await MarkImportBatchAppliedAsync(batch1);
        await _service.ReverseBatchAsync(batch1.ToString("D").ToUpperInvariant()); // soft-deletes the lowercase-id row

        var batch2 = Guid.NewGuid();
        await PlanAndStageAsync([], batch2, DuplicateResolutionPolicy.NewestWins,
            people: [new PersonEntry { Id = id, Name = "Fresh Name After Undo" }]);
        var result = await _service.ApplyBatchAsync(batch2.ToString("D").ToUpperInvariant());
        Assert.IsNull(result);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        // #209: the row is stored under its canonicalized (uppercase) id, not the lowercase id this
        // test declared — UPPER() makes the verification query tolerant of either. #209 also means the
        // stale row is now canonical-uppercase from its very first Add, so ClearStaleAddTargetsAsync's
        // Guid.Parse(...)-based HardDeleteAsync naturally matches it; this test still exercises that
        // exact code path end to end (Add -> reverse -> Add again), just on the now-guaranteed-canonical
        // input the original #173 regression could no longer reproduce a mismatch against.
        var (name, isDeleted) = await conn.QuerySingleAsync<(string Name, int IsDeleted)>(
            "SELECT Name, IsDeleted FROM People WHERE UPPER(Id) = UPPER(@id)", new { id });
        Assert.AreEqual(0, isDeleted, "The stale soft-deleted row must be hard-deleted, letting the fresh Add actually insert");
        Assert.AreEqual("Fresh Name After Undo", name);
    }
}
