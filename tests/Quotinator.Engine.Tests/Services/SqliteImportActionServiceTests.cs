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
using Quotinator.Engine.Models;
using Quotinator.Engine.Repositories;
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
        _service      = new SqliteImportActionService(_actionReader, _coordinator, new SystemChangeLogWriter(_factory));

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

    private static SourceQuote BuildQuote(string id, string source = "Casablanca", string? character = "Rick Blaine") => new()
    {
        Id               = id,
        QuoteText        = "Here's looking at you, kid.",
        OriginalLanguage = "en",
        Source           = source,
        Character        = character,
        Type             = QuoteType.Movie,
    };

    private async Task<IReadOnlyList<SystemImportAction>> PlanAndStageAsync(IReadOnlyList<SourceQuote> quotes, Guid batchId, DuplicateResolutionPolicy policy)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        // Sources.ImportBatchId (and Characters/People/Quotes) is a real FK to ImportBatches — the
        // applier's writes need a genuine row to reference, same as production's own batch-first flow.
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, DateCreated) VALUES (@Id, 'test', 'Import', @now, @now)",
            new { Id = batchId, now });

        var actions = await ImportActionPlanner.PlanAsync(conn, quotes, batchId, policy);
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
}
