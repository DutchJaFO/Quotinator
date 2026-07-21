using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
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
/// Integration tests for <see cref="SqliteQuoteImportService"/> — the live
/// <c>POST /api/v1/import</c>/<c>.../import/preview</c> pipeline. Unlike
/// <see cref="Database.ConflictResolutionTests"/> (which detects duplicates across two files in the
/// same seeding pass), these tests exercise duplicate detection against a quote that already exists
/// in the database from a prior, separate import call — the scenario specific to this service.
/// </summary>
[TestClass]
public class QuoteImportServiceTests
{
    private const string SharedId = "11111111-1111-1111-1111-111111111111";

    private string _tempDir = null!;
    private string _dbPath  = null!;
    private string _backups = null!;
    private SqliteConnectionFactory _factory = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_import_svc_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
        _backups = Path.Combine(_tempDir, "backups");
        _factory = new SqliteConnectionFactory(_dbPath);

        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
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
        var db = new QuotinatorDatabaseInitializer(
            _factory, options, QuotinatorMigrations.All, [], importBatches,
            coordinator, actionService,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance,
            NullLogger<DatabaseInitializer>.Instance, NoOpSourceCacheUpdater.Instance,
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

    private SqliteQuoteImportService CreateService(
        ISystemChangeLogWriter? changeLogWriter = null,
        IReadOnlyDictionary<string, IQuoteSourceConverter>? converters = null,
        ManifestPolicy? configPolicy = null)
    {
        var importBatches  = new SqliteImportBatchRepository(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var actionReader   = new SystemImportActionReader(_factory);
        var actionWriter   = new SystemImportActionWriter(_factory);
        var coordinator    = new ImportActionResolutionCoordinator(actionReader, actionWriter, _factory);
        var actionService  = new SqliteImportActionService(actionReader, coordinator, changeLogWriter ?? NoOpSystemChangeLogWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Source>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Character>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Person>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            importBatches, _factory);
        return new SqliteQuoteImportService(
            _factory, importBatches, coordinator, actionService, actionReader,
            converters ?? new Dictionary<string, IQuoteSourceConverter>(StringComparer.OrdinalIgnoreCase),
            configPolicy ?? new ManifestPolicy(DuplicateResolutionPolicy.NewestWins));
    }

    private static Stream JsonStream(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    private static string OneQuoteJson(string quote, string source, string? character = null, string[]? genres = null) =>
        JsonSerializer.Serialize(new[]
        {
            new { id = SharedId, quote, source, character, genres = genres ?? [] }
        });

    private async Task<int> CountAsync(string table)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table}");
    }

    private async Task<string> ReadQuoteTextAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return (await conn.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotes WHERE Id = @id", new { id = SharedId }))!;
    }

    // ── Fresh insert ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ImportAsync_FreshDatabase_InsertsNewQuote()
    {
        var service = CreateService();

        var result = await service.ImportAsync(JsonStream(OneQuoteJson("A quote.", "A Source")), "test.json", null, preview: false);

        Assert.AreEqual(1, result.Summary.Total);
        Assert.AreEqual(1, result.Summary.Imported);
        Assert.AreEqual(0, result.Summary.Updated);
        Assert.AreEqual(0, result.Summary.Errors);
        Assert.IsNotNull(result.BatchId);
        Assert.AreEqual(0, result.Conflicts.Count, "A brand-new quote is an Add action, never surfaced as a conflict entry — this is what makes a zero-conflict import map to 200, not 202");
        Assert.AreEqual(1, await CountAsync("Quotes"));
        Assert.AreEqual(1, await CountAsync("ImportBatches"));
        Assert.AreEqual("newest-wins", result.ConflictPolicy, "Response-facing wire value must be kebab-case, matching every other DuplicateResolutionPolicy JSON value in this API");
    }

    // ── Conflict policies against a pre-existing DB row (not a second file) ────

    [TestMethod]
    public async Task ImportAsync_Skip_KeepsExistingRowUnchanged()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.Skip } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        Assert.AreEqual(0, result.Summary.Imported);
        Assert.AreEqual(0, result.Summary.Updated);
        Assert.AreEqual(1, result.Summary.Skipped);
        Assert.AreEqual("Original.", await ReadQuoteTextAsync());
    }

    [TestMethod]
    public async Task ImportAsync_NewestWins_ReplacesExistingRow()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.NewestWins } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        Assert.AreEqual(1, result.Summary.Updated);
        Assert.AreEqual("Updated.", await ReadQuoteTextAsync());
        Assert.AreEqual("resolved", result.Conflicts.Single().Status, "NewestWins auto-resolves immediately — a real conflict can be present without leaving anything pending, so this must still map to 200, not 202");
    }

    [TestMethod]
    public async Task ImportAsync_MergeOurs_TrueConflictKeepsExisting()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.MergeOurs } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        Assert.AreEqual(1, result.Summary.Updated);
        Assert.AreEqual("Original.", await ReadQuoteTextAsync());
    }

    [TestMethod]
    public async Task ImportAsync_MergeTheirs_TrueConflictTakesIncoming()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.MergeTheirs } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        Assert.AreEqual(1, result.Summary.Updated);
        Assert.AreEqual("Updated.", await ReadQuoteTextAsync());
        Assert.AreEqual("merge-theirs", result.Conflicts.Single().AppliedPolicy, "Response-facing wire value must be kebab-case, matching every other DuplicateResolutionPolicy JSON value in this API");
    }

    // ── #55/#165: CompletenessStatus / NoValueKnown ──────────────────────────

    [TestMethod]
    public async Task ImportAsync_FreshDatabase_NoValueKnownEmptyAndCompletenessAlreadyNeedsReview()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("A quote.", "A Source")), "test.json", null, preview: false);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var (completenessStatus, noValueKnown) = await conn.QuerySingleAsync<(string CompletenessStatus, string NoValueKnown)>(
            "SELECT CompletenessStatus, NoValueKnown FROM Quotes WHERE Id = @id", new { id = SharedId });

        // #165: nothing currently populates NoValueKnown with real per-field markers at creation, so
        // it's always empty for a brand-new row — which CompletenessGuard.ComputeNextStatus correctly
        // treats the same as "every field just got filled in," transitioning straight to NeedsReview
        // rather than sitting as Incomplete forever with nothing to ever trigger a later transition.
        Assert.AreEqual("NeedsReview", completenessStatus);
        Assert.AreEqual("[]", noValueKnown, "A brand-new row must default NoValueKnown to an empty JSON array");
    }

    /// <summary>
    /// #168: prior to that fix, this test asserted the opposite of what it's named — a `Complete` row
    /// was silently rewritten by any non-Skip/Review policy, and only the completeness columns
    /// themselves were checked as "surviving." Rewritten to assert what "survives reimport unchanged"
    /// actually requires: the row itself, not only its completeness bookkeeping, must be untouched
    /// until a human decides the held action. `MergeOurs` is deliberately excluded from this DataRow
    /// set — see <see cref="ImportAsync_ExistingRowMarkedComplete_MergeOursPolicy_NeverBlocksSinceExistingWins"/>
    /// for why it behaves differently.
    /// </summary>
    [TestMethod]
    [DataRow(DuplicateResolutionPolicy.NewestWins)]
    [DataRow(DuplicateResolutionPolicy.MergeTheirs)]
    public async Task ImportAsync_ExistingRowMarkedComplete_SurvivesReimportUnchanged(DuplicateResolutionPolicy policy)
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await conn.ExecuteAsync(
                "UPDATE Quotes SET CompletenessStatus = 'Complete', NoValueKnown = '[\"date\"]' WHERE Id = @id",
                new { id = SharedId });
        }

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = policy } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        Assert.AreEqual(0, result.Summary.Updated, "A Complete row's field change must be held, not silently applied, regardless of policy");
        Assert.AreEqual(1, result.PendingActionIds.Count, "The held action must be surfaced as pending/blocked");

        using var conn2 = new SqliteConnection($"Data Source={_dbPath}");
        conn2.Open();
        var (quoteText, completenessStatus, noValueKnown) = await conn2.QuerySingleAsync<(string QuoteText, string CompletenessStatus, string NoValueKnown)>(
            "SELECT QuoteText, CompletenessStatus, NoValueKnown FROM Quotes WHERE Id = @id", new { id = SharedId });

        Assert.AreEqual("Original.", quoteText, "The row itself must be untouched — this is what 'survives reimport unchanged' actually means");
        Assert.AreEqual("Complete", completenessStatus, "A human's completed review must survive a held re-import attempt");
        Assert.AreEqual("[\"date\"]", noValueKnown, "Confirmed no-value-known markers must survive a held re-import attempt");
    }

    /// <summary>
    /// #168: <c>MergeOurs</c> resolves any true conflict (both sides non-empty and differing) by
    /// keeping the existing value — so against a <c>Complete</c> row, the resolved write is always
    /// identical to what's already there. Nothing would actually change, so <c>ShouldBlock</c>
    /// correctly never triggers; this is not a gap, it's `MergeOurs`'s own semantics already
    /// protecting a human-confirmed value on every conflicting field.
    /// </summary>
    [TestMethod]
    public async Task ImportAsync_ExistingRowMarkedComplete_MergeOursPolicy_NeverBlocksSinceExistingWins()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await conn.ExecuteAsync("UPDATE Quotes SET CompletenessStatus = 'Complete' WHERE Id = @id", new { id = SharedId });
        }

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.MergeOurs } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        Assert.AreEqual(0, result.PendingActionIds.Count, "MergeOurs keeps the existing value on every conflicting field, so there is nothing to hold");

        using var conn2 = new SqliteConnection($"Data Source={_dbPath}");
        conn2.Open();
        var quoteText = await conn2.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotes WHERE Id = @id", new { id = SharedId });
        Assert.AreEqual("Original.", quoteText, "MergeOurs must keep the existing (Complete) value, not the incoming one");
    }

    /// <summary>
    /// #165 regression guard, found live via T2: importing a batch containing a <c>Blocked</c> Source
    /// action (a <c>sources[]</c> correction against an already-<c>Complete</c> row) must report
    /// <c>PendingActionIds</c> non-empty and must genuinely hold the whole batch — an unrelated
    /// brand-new quote sharing the same batch must not be written either. The bug this guards against:
    /// <c>ImportResultResponse</c> originally only reflected Quote-specific <c>Conflicts</c>, so a
    /// batch held purely by a Source Blocked action silently reported success with nothing actually
    /// applied.
    /// </summary>
    [TestMethod]
    public async Task ImportAsync_BlockedSourceInBatch_PendingActionIdsNonEmptyAndWholeBatchHeld()
    {
        var service = CreateService();
        const string sourceId = "bbbbbbbb-2222-4222-8222-222222222222";

        var firstFile = """
            {
              "quotes": [{"id":"__SHARED_ID__","quote":"Original.","source":"T2 Regression Movie","type":"movie","originalLanguage":"en","genres":[],"translations":{}}],
              "sources": [{"id":"__SOURCE_ID__","title":"T2 Regression Movie","type":"movie"}]
            }
            """
            .Replace("__SHARED_ID__", SharedId).Replace("__SOURCE_ID__", sourceId);
        await service.ImportAsync(JsonStream(firstFile), "first.json", null, preview: false);

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            // #209: the row is stored under its canonicalized (uppercase) id, not the lowercase id
            // this test declared — UPPER() makes the update tolerant of either.
            await conn.ExecuteAsync("UPDATE Sources SET CompletenessStatus = 'Complete' WHERE UPPER(Id) = UPPER(@id)", new { id = sourceId });
        }

        var unrelatedQuoteId = "dddddddd-4444-4444-8444-444444444444";
        var secondFile = """
            {
              "quotes": [
                {"id":"__SHARED_ID__","quote":"Original.","source":"T2 Regression Movie","type":"movie","originalLanguage":"en","genres":[],"translations":{}},
                {"id":"__UNRELATED_ID__","quote":"An unrelated new quote.","source":"T2 Unrelated Movie","type":"movie","originalLanguage":"en","genres":[],"translations":{}}
              ],
              "sources": [{"id":"__SOURCE_ID__","title":"T2 Regression Movie (Corrected)","type":"movie"}]
            }
            """
            .Replace("__SHARED_ID__", SharedId).Replace("__SOURCE_ID__", sourceId).Replace("__UNRELATED_ID__", unrelatedQuoteId);
        var result = await service.ImportAsync(JsonStream(secondFile), "second.json", null, preview: false);

        Assert.IsTrue(result.PendingActionIds.Count > 0, "A Blocked Source action must be reflected in PendingActionIds");

        using var verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        verifyConn.Open();
        var unrelatedQuoteCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Quotes WHERE Id = @id", new { id = unrelatedQuoteId });
        Assert.AreEqual(0, unrelatedQuoteCount, "The whole batch must be held — an unrelated quote sharing the batch must not be written either");
    }


    [TestMethod]
    public async Task ImportAsync_Review_BehavesLikeSkip()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.Review } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        Assert.AreEqual(1, result.Summary.Skipped);
        Assert.AreEqual("Original.", await ReadQuoteTextAsync());
        Assert.AreEqual("pending", result.Conflicts.Single().Status);
    }

    // ── #56: System_ChangeLog ────────────────────────────────────────────────

    [TestMethod]
    public async Task ImportAsync_FreshDatabase_WritesCreatedChangeLogRowWithImportInitiator()
    {
        var changeLogWriter = new SystemChangeLogWriter(_factory);
        var service = CreateService(changeLogWriter: changeLogWriter);

        var result = await service.ImportAsync(JsonStream(OneQuoteJson("A quote.", "A Source")), "test.json", null, preview: false);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var row = await conn.QuerySingleAsync<(string InitiatedByType, string InitiatedById, string Action)>(
            "SELECT InitiatedByType, InitiatedById, Action FROM System_ChangeLog WHERE EntityType = 'quote' AND EntityId = @id",
            new { id = SharedId });

        Assert.AreEqual("Import", row.InitiatedByType);
        Assert.AreEqual(result.BatchId!.Value.ToString("D"), row.InitiatedById);
        Assert.AreEqual("Created", row.Action);
    }

    [TestMethod]
    public async Task ImportAsync_NewestWins_WritesModifiedChangeLogRowWithSameImportBatchId()
    {
        var changeLogWriter = new SystemChangeLogWriter(_factory);
        var service = CreateService(changeLogWriter: changeLogWriter);
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.NewestWins } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var rows = (await conn.QueryAsync<(string InitiatedById, string Action)>(
            "SELECT InitiatedById, Action FROM System_ChangeLog WHERE EntityType = 'quote' AND EntityId = @id ORDER BY OccurredAt",
            new { id = SharedId })).ToList();

        Assert.AreEqual(2, rows.Count, "One Created row from the first import, one Modified row from the newest-wins rewrite");
        Assert.AreEqual("Created", rows[0].Action);
        Assert.AreEqual("Modified", rows[1].Action);
        Assert.AreEqual(result.BatchId!.Value.ToString("D"), rows[1].InitiatedById,
            "The Modified row's InitiatedById must be the second import's own batch, not the first");
    }

    [TestMethod]
    public async Task ImportAsync_Skip_WritesNoModifiedChangeLogRow()
    {
        var changeLogWriter = new SystemChangeLogWriter(_factory);
        var service = CreateService(changeLogWriter: changeLogWriter);
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.Skip } };
        await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var actions = (await conn.QueryAsync<string>(
            "SELECT Action FROM System_ChangeLog WHERE EntityType = 'quote' AND EntityId = @id",
            new { id = SharedId })).ToList();

        CollectionAssert.AreEqual(new[] { "Created" }, actions, "Skip never executes the UPDATE, so no Modified row should exist");
    }

    [TestMethod]
    public async Task ImportAsync_PreviewWithNewRow_NoChangeLogRowPersisted()
    {
        var changeLogWriter = new SystemChangeLogWriter(_factory);
        var service = CreateService(changeLogWriter: changeLogWriter);

        await service.ImportAsync(JsonStream(OneQuoteJson("A quote.", "A Source")), "test.json", null, preview: true);

        Assert.AreEqual(0, await CountAsync("System_ChangeLog"), "Rolled back — no change-log row persisted for a preview run");
    }

    // ── Preview ──────────────────────────────────────────────────────────────

    // #154 revision: preview now stages a real, inspectable batch instead of rolling everything
    // back — "nothing persisted" is no longer the contract (see 154-import-staging-plan.md Section
    // "Explicit behavior changes"). These two tests are updated, not left unmodified, because they
    // directly assert the old rollback contract.

    [TestMethod]
    public async Task ImportAsync_Preview_StagesButNeverApplies()
    {
        var service = CreateService();

        var result = await service.ImportAsync(JsonStream(OneQuoteJson("A quote.", "A Source")), "test.json", null, preview: true);

        Assert.IsTrue(result.Preview);
        Assert.IsNotNull(result.BatchId, "Preview now stages a real batch, unlike the old rollback contract");
        Assert.AreEqual(1, result.Summary.Imported, "Response still reports what would have happened");
        Assert.AreEqual(0, await CountAsync("Quotes"), "Staging never applies — no quote written");
        Assert.AreEqual(1, await CountAsync("ImportBatches"), "The batch itself is durably staged");
        Assert.AreEqual(2, await CountAsync("System_ImportActions"), "The planned Quote and Source Add actions are both durably staged");
    }

    [TestMethod]
    public async Task ImportAsync_PreviewWithConflict_StagesButDoesNotApply()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", null, preview: true);

        Assert.AreEqual(1, result.Conflicts.Count, "Response reflects the conflict that would have been detected");
        Assert.IsTrue(await CountAsync("System_ImportActions") > 0, "The Modify action is durably staged, not rolled back");
        Assert.AreEqual("Original.", await ReadQuoteTextAsync(), "Never applied — original row untouched");
    }

    // ── ApplyStagedBatchAsync — batchId mode, the alias for POST /import/actions/apply ─────────

    [TestMethod]
    public async Task ApplyStagedBatchAsync_PreviouslyStagedBatch_AppliesItAndReturns200Shape()
    {
        var service = CreateService();
        var previewResult = await service.ImportAsync(JsonStream(OneQuoteJson("A quote.", "A Source")), "test.json", null, preview: true);
        Assert.AreEqual(0, await CountAsync("Quotes"), "Still just staged, not applied");

        var applyResult = await service.ApplyStagedBatchAsync(previewResult.BatchId!.Value);

        Assert.AreEqual(previewResult.BatchId, applyResult.BatchId);
        Assert.IsFalse(applyResult.Preview);
        Assert.AreEqual(1, applyResult.Summary.Imported);
        Assert.AreEqual(0, applyResult.Conflicts.Count(c => c.Status == "pending"), "Nothing pending — endpoint would return 200");
        Assert.AreEqual(1, await CountAsync("Quotes"), "Applying the staged batch actually writes the quote");
    }

    [TestMethod]
    public async Task ApplyStagedBatchAsync_BatchWithPendingConflict_StillReportsItPending()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);
        var previewResult = await service.ImportAsync(
            JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json",
            new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.Review } },
            preview: true);

        var applyResult = await service.ApplyStagedBatchAsync(previewResult.BatchId!.Value);

        Assert.AreEqual(1, applyResult.Conflicts.Count(c => c.Status == "pending"), "Still pending — endpoint would return 202, not silently succeed");
        Assert.AreEqual("Original.", await ReadQuoteTextAsync(), "Never applied — original row untouched");
    }

    [TestMethod]
    public async Task ApplyStagedBatchAsync_UnknownBatchId_ThrowsImportBatchNotFoundException()
    {
        var service = CreateService();

        await Assert.ThrowsExactlyAsync<ImportBatchNotFoundException>(
            () => service.ApplyStagedBatchAsync(Guid.NewGuid()));
    }

    // ── Row-level error tolerance ────────────────────────────────────────────

    [TestMethod]
    public async Task ImportAsync_OneRowMissingSource_SkipsItButImportsTheRest()
    {
        var service = CreateService();
        var json = """
            [
              {"id":"11111111-1111-1111-1111-111111111111","quote":"Missing a source","source":""},
              {"id":"22222222-2222-2222-2222-222222222222","quote":"A real quote.","source":"A Real Source"}
            ]
            """;

        var result = await service.ImportAsync(JsonStream(json), "test.json", null, preview: false);

        Assert.AreEqual(2, result.Summary.Total);
        Assert.AreEqual(1, result.Summary.Imported);
        Assert.AreEqual(1, result.Summary.Errors);
        Assert.AreEqual(1, result.Errors.Single().Row);
        Assert.AreEqual(1, await CountAsync("Quotes"));
    }

    [TestMethod]
    public async Task ImportAsync_FileWithNoQuotes_ThrowsQuoteImportValidationException()
    {
        var service = CreateService();

        await Assert.ThrowsExactlyAsync<QuoteImportValidationException>(
            () => service.ImportAsync(JsonStream("[]"), "test.json", null, preview: false));
    }

    [TestMethod]
    public async Task ImportAsync_MalformedJson_ThrowsQuoteImportValidationException()
    {
        var service = CreateService();

        await Assert.ThrowsExactlyAsync<QuoteImportValidationException>(
            () => service.ImportAsync(JsonStream("{ not json"), "test.json", null, preview: false));
    }

    // ── Converter path ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task ImportAsync_UnknownConverterName_ThrowsUnknownConverterException()
    {
        var service = CreateService();
        var settings = new ImportRequestSettingsDto { Converter = "does-not-exist" };

        var ex = await Assert.ThrowsExactlyAsync<UnknownConverterException>(
            () => service.ImportAsync(JsonStream("irrelevant"), "test.json", settings, preview: false));
        Assert.AreEqual("does-not-exist", ex.ConverterName);
    }

    // ── #68: conversations via POST /import ────────────────────────────────

    private static string ConversationJson(string conversationId, string quoteId, string stageDirectionId) =>
        $$"""
        {
          "quotes": [{"id":"{{quoteId}}","quote":"A quote.","source":"A Source"}],
          "stageDirections": [{"id":"{{stageDirectionId}}","text":"[A stage direction]"}],
          "conversations": [{
            "id":"{{conversationId}}",
            "lines":[
              {"order":1,"type":"stage_direction","stageDirectionId":"{{stageDirectionId}}"},
              {"order":2,"type":"quote","quoteId":"{{quoteId}}"}
            ]
          }]
        }
        """;

    [TestMethod]
    public async Task ImportAsync_ExtendedFormatFile_StagesAndAppliesConversationAndStageDirection()
    {
        var service = CreateService();
        const string conversationId   = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        const string quoteId          = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        const string stageDirectionId = "cccccccc-cccc-cccc-cccc-cccccccccccc";

        var result = await service.ImportAsync(
            JsonStream(ConversationJson(conversationId, quoteId, stageDirectionId)), "conversation.json", null, preview: false);

        Assert.AreEqual(0, result.Summary.Errors);
        Assert.AreEqual(1, await CountAsync("Conversations"));
        Assert.AreEqual(1, await CountAsync("StageDirections"));
        Assert.AreEqual(2, await CountAsync("ConversationLines"));

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var actionTypes = (await conn.QueryAsync<string>(
            "SELECT DISTINCT EntityType FROM System_ImportActions WHERE EntityType IN ('Conversation', 'StageDirection');")).ToList();
        CollectionAssert.AreEquivalent(new[] { "Conversation", "StageDirection" }, actionTypes);
    }

    [TestMethod]
    public async Task ImportAsync_SameExtendedFormatFileImportedTwice_DoesNotDuplicateConversationOrStageDirection()
    {
        var service = CreateService();
        const string conversationId   = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        const string quoteId          = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        const string stageDirectionId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
        var json = ConversationJson(conversationId, quoteId, stageDirectionId);

        await service.ImportAsync(JsonStream(json), "first.json", null, preview: false);
        // NewestWins (this fixture's default policy) re-applies the Quote as a Modify — harmless
        // here — but Conversation/StageDirection are Add-only and id-keyed, so re-staging the same
        // ids a second time must detect they already exist and skip, not violate a PK/UNIQUE constraint.
        await service.ImportAsync(JsonStream(json), "second.json", null, preview: false);

        Assert.AreEqual(1, await CountAsync("Conversations"));
        Assert.AreEqual(1, await CountAsync("StageDirections"));
        Assert.AreEqual(2, await CountAsync("ConversationLines"), "Re-importing must not double the line count either");
    }

    [TestMethod]
    public async Task ImportAsync_RegisteredConverter_ConvertsBeforeImporting()
    {
        var converters = new Dictionary<string, IQuoteSourceConverter>(StringComparer.OrdinalIgnoreCase)
        {
            ["passthrough"] = new PassthroughTestConverter()
        };
        var service = CreateService(converters: converters);
        var settings = new ImportRequestSettingsDto { Converter = "passthrough" };

        var result = await service.ImportAsync(
            JsonStream(OneQuoteJson("Converted quote.", "A Source")), "raw.txt", settings, preview: false);

        Assert.AreEqual(1, result.Summary.Imported);
        Assert.AreEqual("Converted quote.", await ReadQuoteTextAsync());
    }

    [TestMethod]
    public async Task ImportAsync_ConverterWithOptions_PassesOptionsToConvertAsync()
    {
        var passthrough = new PassthroughTestConverter();
        var converters = new Dictionary<string, IQuoteSourceConverter>(StringComparer.OrdinalIgnoreCase)
        {
            ["passthrough"] = passthrough
        };
        var service = CreateService(converters: converters);
        var converterOptions = JsonSerializer.Deserialize<JsonElement>("""{"propertyMapping": {"source": "movie"}}""");
        var settings = new ImportRequestSettingsDto { Converter = "passthrough", ConverterOptions = converterOptions };

        await service.ImportAsync(
            JsonStream(OneQuoteJson("Converted quote.", "A Source")), "raw.txt", settings, preview: false);

        Assert.IsNotNull(passthrough.LastReceivedOptions);
        Assert.AreEqual("movie", passthrough.LastReceivedOptions!.Value.GetProperty("propertyMapping").GetProperty("source").GetString());
    }

    /// <summary>Trivial converter that copies its input verbatim — the input is already canonical JSON for this test's purposes.</summary>
    private sealed class PassthroughTestConverter : IQuoteSourceConverter
    {
        public string Name => "passthrough";

        public JsonElement? LastReceivedOptions { get; private set; }

        public async Task ConvertAsync(string inputPath, string outputPath, JsonElement? options = null, CancellationToken cancellationToken = default)
        {
            LastReceivedOptions = options;
            await File.WriteAllTextAsync(outputPath, await File.ReadAllTextAsync(inputPath, cancellationToken), cancellationToken);
        }
    }
}
