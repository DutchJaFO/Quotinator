using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Database;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Services;

namespace Quotinator.Engine.Tests.Services;

/// <summary>
/// Exercises <see cref="SqliteQuoteService.GetAll"/> against a real, freshly-migrated SQLite schema —
/// in particular #195's <c>pageSize = 0</c> fix, which mirrors #193's already-verified <c>LIMIT -1</c>
/// pattern but is a separate hand-written query (<c>Sql.Quotes.SelectPaged</c>), not covered by any
/// existing test before this issue.
/// </summary>
[TestClass]
public class SqliteQuoteServiceTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private SqliteConnectionFactory _factory = null!;
    private SqliteQuoteService _service = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_quote_service_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
        _factory = new SqliteConnectionFactory(_dbPath);
        _service = new SqliteQuoteService(_factory);

        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = Path.Combine(_tempDir, "backups") };
        var importBatches = new SqliteImportBatchRepository(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var actionReader   = new SystemImportActionReader(_factory);
        var actionWriter   = new SystemImportActionWriter(_factory);
        var coordinator    = new ImportActionResolutionCoordinator(actionReader, actionWriter, _factory);
        var actionService  = new SqliteImportActionService(actionReader, coordinator, new SystemChangeLogWriter(_factory),
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

        var sourceRepo = new SqliteRestorableRepository<Source>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var source = new Source
        {
            Title = "Test Source",
            Type = new SafeValue<QuoteType?>(nameof(QuoteType.Movie), QuoteType.Movie),
            CompletenessStatus = new SafeValue<CompletenessStatus?>(nameof(CompletenessStatus.Incomplete), CompletenessStatus.Incomplete),
        };
        await sourceRepo.InsertAsync(source);

        var quoteRepo = new SqliteRestorableRepository<QuoteEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        for (var i = 0; i < 5; i++)
            await quoteRepo.InsertAsync(new QuoteEntity
            {
                QuoteText = $"Quote {i}",
                SourceId = source.Id,
                CompletenessStatus = new SafeValue<CompletenessStatus?>(nameof(CompletenessStatus.Incomplete), CompletenessStatus.Incomplete),
            });
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void GetAll_PageSizeZero_ReturnsEveryRowNotZeroRows()
    {
        var result = _service.GetAll(1, 0);

        Assert.AreEqual(5, result.Items.Count, "pageSize = 0 must reach SQLite as LIMIT -1, not a literal LIMIT 0");
        Assert.AreEqual(5, result.TotalCount);
    }

    [TestMethod]
    public void GetAll_PageSizeZero_ReportsEffectivePageSize()
    {
        var result = _service.GetAll(1, 0);

        Assert.AreEqual(5, result.PageSize, "PageSize must report the effective count actually returned, not the literal 0 requested");
    }

    [TestMethod]
    public void GetAll_PageSizeNonZero_StillPaginatesNormally()
    {
        var result = _service.GetAll(1, 2);

        Assert.AreEqual(2, result.Items.Count);
        Assert.AreEqual(5, result.TotalCount);
        Assert.AreEqual(2, result.PageSize);
    }
}
