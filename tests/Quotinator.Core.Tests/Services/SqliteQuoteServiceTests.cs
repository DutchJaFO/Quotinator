using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Models;
using Quotinator.Core.Queries;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Services;

namespace Quotinator.Core.Tests.Services;

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

    // ── #192: Series/Universe enrichment and filters ────────────────────────

    private async Task<UniverseEntity> InsertUniverseAsync(string name)
    {
        var repo = new SqliteRestorableRepository<UniverseEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var universe = new UniverseEntity
        {
            Name = name,
            CompletenessStatus = new SafeValue<CompletenessStatus?>(nameof(CompletenessStatus.Incomplete), CompletenessStatus.Incomplete),
        };
        await repo.InsertAsync(universe);
        return universe;
    }

    private async Task<SeriesEntity> InsertSeriesAsync(string name, Guid? universeId = null)
    {
        var repo = new SqliteRestorableRepository<SeriesEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var series = new SeriesEntity
        {
            Name = name,
            UniverseId = universeId,
            CompletenessStatus = new SafeValue<CompletenessStatus?>(nameof(CompletenessStatus.Incomplete), CompletenessStatus.Incomplete),
        };
        await repo.InsertAsync(series);
        return series;
    }

    private async Task<Source> InsertSourceAsync(string title, Guid? seriesId = null)
    {
        var repo = new SqliteRestorableRepository<Source>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var source = new Source
        {
            Title = title,
            Type = new SafeValue<QuoteType?>(nameof(QuoteType.Movie), QuoteType.Movie),
            SeriesId = seriesId,
            CompletenessStatus = new SafeValue<CompletenessStatus?>(nameof(CompletenessStatus.Incomplete), CompletenessStatus.Incomplete),
        };
        await repo.InsertAsync(source);
        return source;
    }

    private async Task<QuoteEntity> InsertQuoteAsync(Guid sourceId, string text)
    {
        var repo = new SqliteRestorableRepository<QuoteEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var quote = new QuoteEntity
        {
            QuoteText = text,
            SourceId = sourceId,
            CompletenessStatus = new SafeValue<CompletenessStatus?>(nameof(CompletenessStatus.Incomplete), CompletenessStatus.Incomplete),
        };
        await repo.InsertAsync(quote);
        return quote;
    }

    [TestMethod]
    public async Task GetById_SourceInSeriesWithUniverse_ResponseCarriesBoth()
    {
        var universe = await InsertUniverseAsync("Middle Earth");
        var series   = await InsertSeriesAsync("The Lord of the Rings", universe.Id);
        var source   = await InsertSourceAsync("The Fellowship of the Ring", series.Id);
        var quote    = await InsertQuoteAsync(source.Id, "One does not simply walk into Mordor.");

        var result = _service.GetById(quote.Id.ToString("D"));

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Series);
        Assert.AreEqual("The Lord of the Rings", result.Series!.Name);
        Assert.IsNotNull(result.Universe);
        Assert.AreEqual("Middle Earth", result.Universe!.Name);
    }

    [TestMethod]
    public async Task GetById_SourceWithNoSeries_ReturnsQuoteWithNullSeriesAndUniverse()
    {
        // TestInitialize's "Test Source" has no SeriesId.
        var result = _service.GetAll(1, 1).Items[0];
        var byId   = _service.GetById(result.Id);

        Assert.IsNotNull(byId);
        Assert.IsNull(byId!.Series);
        Assert.IsNull(byId.Universe);
    }

    [TestMethod]
    public async Task GetById_SeriesWithNoUniverse_ReturnsSeriesWithNullUniverse()
    {
        var series = await InsertSeriesAsync("Standalone Series", universeId: null);
        var source = await InsertSourceAsync("A Standalone Film", series.Id);
        var quote  = await InsertQuoteAsync(source.Id, "A quote with a series but no universe.");

        var result = _service.GetById(quote.Id.ToString("D"));

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Series);
        Assert.AreEqual("Standalone Series", result.Series!.Name);
        Assert.IsNull(result.Universe);
    }

    [TestMethod]
    public async Task GetById_SeriesSoftDeleted_ReturnsNullSeriesAndUniverse()
    {
        var series = await InsertSeriesAsync("Soon To Be Deleted Series");
        var source = await InsertSourceAsync("A Film In A Deleted Series", series.Id);
        var quote  = await InsertQuoteAsync(source.Id, "A quote whose series gets soft-deleted.");

        var seriesRepo = new SqliteRestorableRepository<SeriesEntity>(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        await seriesRepo.SoftDeleteAsync(series.Id);

        var result = _service.GetById(quote.Id.ToString("D"));

        Assert.IsNotNull(result);
        Assert.IsNull(result!.Series, "A soft-deleted Series must never leak through as a dangling reference.");
        Assert.IsNull(result.Universe);
    }

    // ── #210: Quotes.Id case-insensitive lookup ─────────────────────────────

    /// <summary>
    /// Unlike Source/People (which canonicalize to uppercase), a Quote's canonical stored form is
    /// lowercase (<c>QuoteIdentity.StableId</c>'s pinned convention) — inserted here via raw SQL rather
    /// than <see cref="InsertQuoteAsync"/>, whose generic repository path would force an uppercase id
    /// via <c>GuidHandler</c> and not actually exercise a lowercase-stored row. Before #210,
    /// <c>Sql.Quotes.SelectById()</c> had no <c>UPPER()</c> wrapping at all — the one fully-unmitigated
    /// case-sensitivity gap in the codebase, closed by this test.
    /// </summary>
    [TestMethod]
    public async Task GetById_UppercaseUrlIdAgainstLowercaseStoredQuote_StillResolves()
    {
        var source      = await InsertSourceAsync("A Film With A Lowercase-Stored Quote Id");
        var lowercaseId = Guid.NewGuid().ToString("D");

        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        await connection.ExecuteAsync(Sql.Quotes.Insert, new
        {
            Id               = lowercaseId,
            QuoteText        = "A quote whose id is stored in canonical lowercase.",
            OriginalLanguage = "en",
            SourceId         = source.Id,
            CharacterId      = (string?)null,
            PersonId         = (string?)null,
            ImportBatchId    = (string?)null,
            DateCreated      = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        });

        var result = _service.GetById(lowercaseId.ToUpperInvariant());

        Assert.IsNotNull(result,
            "GET /quotes/{id} must resolve regardless of URL casing (#210) — the previously fully-unmitigated gap.");
    }

    [TestMethod]
    public async Task GetAll_SeriesFilter_ReturnsOnlyThatSeriesQuotes()
    {
        var series      = await InsertSeriesAsync("The Filtered Series");
        var source      = await InsertSourceAsync("A Film In The Filtered Series", series.Id);
        var quote       = await InsertQuoteAsync(source.Id, "The only quote that should match.");

        var result = _service.GetAll(1, 10, seriesId: series.Id);

        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual(quote.Id.ToString("D"), result.Items[0].Id);
    }

    [TestMethod]
    public async Task GetAll_UniverseFilter_ReturnsQuotesAcrossEverySeriesInThatUniverse()
    {
        var universe = await InsertUniverseAsync("A Shared Universe");
        var seriesA  = await InsertSeriesAsync("Series A", universe.Id);
        var seriesB  = await InsertSeriesAsync("Series B", universe.Id);
        var sourceA  = await InsertSourceAsync("Film A", seriesA.Id);
        var sourceB  = await InsertSourceAsync("Film B", seriesB.Id);
        var quoteA   = await InsertQuoteAsync(sourceA.Id, "Quote from Series A.");
        var quoteB   = await InsertQuoteAsync(sourceB.Id, "Quote from Series B.");

        var result = _service.GetAll(1, 10, universeId: universe.Id);

        var ids = result.Items.Select(i => i.Id).ToList();
        CollectionAssert.Contains(ids, quoteA.Id.ToString("D"));
        CollectionAssert.Contains(ids, quoteB.Id.ToString("D"));
    }

    [TestMethod]
    public async Task GetRandom_SeriesFilter_ReturnsOnlyThatSeriesQuotes()
    {
        var series = await InsertSeriesAsync("The Random-Filtered Series");
        var source = await InsertSourceAsync("A Film In The Random-Filtered Series", series.Id);
        var quote  = await InsertQuoteAsync(source.Id, "The only quote random selection can pick.");

        var result = _service.GetRandom(10, seriesId: series.Id);

        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual(quote.Id.ToString("D"), result.Items[0].Id);
    }

    [TestMethod]
    public async Task GetRandom_UniverseFilter_ReturnsOnlyThatUniverseQuotes()
    {
        var universe = await InsertUniverseAsync("The Random-Filtered Universe");
        var series   = await InsertSeriesAsync("A Series In It", universe.Id);
        var source   = await InsertSourceAsync("A Film In It", series.Id);
        var quote    = await InsertQuoteAsync(source.Id, "The only quote random selection can pick.");

        var result = _service.GetRandom(10, universeId: universe.Id);

        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual(quote.Id.ToString("D"), result.Items[0].Id);
    }
}
