using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Enums;
using Quotinator.Core.Models;
using Quotinator.Core.Queries;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
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
        _service = new SqliteQuoteService(
            _factory,
            unicodeAwareSearch: false,
            new JoinQueryRepository<QuoteRow>(_factory, new QuoteLineStrategy()),
            new JoinQueryRepository<StageDirectionLineRow>(_factory, new StageDirectionLineStrategy()),
            new JoinQueryRepository<SoundCueLineRow>(_factory, new SoundCueLineStrategy()));

        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = Path.Combine(_tempDir, "backups") };
        var importBatches = new SqliteImportBatchRepository(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        var actionReader   = new ImportActionReader(_factory);
        var actionWriter   = new ImportActionWriter(_factory);
        var coordinator    = new ImportActionResolutionCoordinator(actionReader, actionWriter, _factory);
        var actionService  = new SqliteImportActionService(actionReader, coordinator, actionWriter, NoOpAuditEntryWriter.Instance, new ChangeWriter(_factory),
            new SqliteRestorableRepository<QuoteEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SourceEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<CharacterEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<PersonEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            importBatches, _factory);

        var db = new QuotinatorDatabaseInitializer(_factory, options, QuotinatorMigrations.All, [], importBatches,
            coordinator, actionService, actionWriter, NoOpAuditEntryWriter.Instance,
            NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance, NoOpSourceCacheUpdater.Instance,
            autoUpdateSources: false,
            autoPurgeBundledImportActions: false, autoPurgeUserImportActions: false,
            NoOpRuleFileOverridePathResolver.Instance, NoOpSourceFileOverrideRegistry.Instance, NoOpFileResourceRepository.Instance,
            NoOpNotificationReader.Instance, NoOpNotificationWriter.Instance, NoOpNotificationTextSource.Instance,
            QuotinatorMigrations.Baseline);
        await db.InitialiseAsync();

        var sourceRepo = new SqliteRestorableRepository<SourceEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        var source = new SourceEntity
        {
            Title = "Test Source",
            Type = new SafeValue<QuoteType?>(nameof(QuoteType.Movie), QuoteType.Movie),
            CompletenessStatus = new SafeValue<CompletenessStatus?>(nameof(CompletenessStatus.Incomplete), CompletenessStatus.Incomplete),
        };
        await sourceRepo.InsertAsync(source);

        var quoteRepo = new SqliteRestorableRepository<QuoteEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
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
    public async Task GetAll_PageSizeZero_ReturnsEveryRowNotZeroRows()
    {
        var result = await _service.GetAll(1, 0);

        Assert.HasCount(5, result.Items, "pageSize = 0 must reach SQLite as LIMIT -1, not a literal LIMIT 0");
        Assert.AreEqual(5, result.TotalCount);
    }

    [TestMethod]
    public async Task GetAll_PageSizeZero_ReportsEffectivePageSize()
    {
        var result = await _service.GetAll(1, 0);

        Assert.AreEqual(5, result.PageSize, "PageSize must report the effective count actually returned, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetAll_PageSizeNonZero_StillPaginatesNormally()
    {
        var result = await _service.GetAll(1, 2);

        Assert.HasCount(2, result.Items);
        Assert.AreEqual(5, result.TotalCount);
        Assert.AreEqual(2, result.PageSize);
    }

    // ── #192: Series/Universe enrichment and filters ────────────────────────

    private async Task<UniverseEntity> InsertUniverseAsync(string name)
    {
        var repo = new SqliteRestorableRepository<UniverseEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
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
        var repo = new SqliteRestorableRepository<SeriesEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        var series = new SeriesEntity
        {
            Name = name,
            UniverseId = universeId,
            CompletenessStatus = new SafeValue<CompletenessStatus?>(nameof(CompletenessStatus.Incomplete), CompletenessStatus.Incomplete),
        };
        await repo.InsertAsync(series);
        return series;
    }

    private async Task<SourceEntity> InsertSourceAsync(string title, Guid? seriesId = null)
    {
        var repo = new SqliteRestorableRepository<SourceEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        var source = new SourceEntity
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
        var repo = new SqliteRestorableRepository<QuoteEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        var quote = new QuoteEntity
        {
            QuoteText = text,
            SourceId = sourceId,
            CompletenessStatus = new SafeValue<CompletenessStatus?>(nameof(CompletenessStatus.Incomplete), CompletenessStatus.Incomplete),
        };
        await repo.InsertAsync(quote);
        return quote;
    }

    private async Task InsertQuoteTranslationAsync(Guid quoteId, string language, string quoteText)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync(Sql.QuoteTranslations.Insert, new
        {
            Id          = Guid.NewGuid().ToString(),
            QuoteId     = quoteId.ToString("D"),
            Language    = language,
            QuoteText   = quoteText,
            DateCreated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        });
    }

    /// <summary>
    /// #216 fix: `?lang=NL` must match a translation stored under `Language = 'nl'` the same way
    /// `?lang=nl` does — before the fix, the JOIN's `Language = @lang` comparison was case-sensitive,
    /// so an uppercase query parameter silently fell back to the original language instead of
    /// returning the translation.
    /// </summary>
    [TestMethod]
    public async Task GetById_UppercaseLang_StillMatchesLowercaseStoredTranslation()
    {
        var source = await InsertSourceAsync("A Film With A Dutch Translation");
        var quote  = await InsertQuoteAsync(source.Id, "Original English text.");
        await InsertQuoteTranslationAsync(quote.Id, "nl", "Nederlandse tekst.");

        var result = await _service.GetById(quote.Id.ToString("D"), lang: "NL");

        Assert.IsNotNull(result);
        Assert.AreEqual("Nederlandse tekst.", result.Quote, "Uppercase ?lang=NL must still resolve the lowercase-stored 'nl' translation");
        Assert.AreEqual("nl", result.Language, "The returned Language must be canonically lowercase, not echo the caller's uppercase casing");
        Assert.IsTrue(result.IsTranslated);
    }

    [TestMethod]
    public async Task GetById_SourceInSeriesWithUniverse_ResponseCarriesBoth()
    {
        var universe = await InsertUniverseAsync("Middle Earth");
        var series   = await InsertSeriesAsync("The Lord of the Rings", universe.Id);
        var source   = await InsertSourceAsync("The Fellowship of the Ring", series.Id);
        var quote    = await InsertQuoteAsync(source.Id, "One does not simply walk into Mordor.");

        var result = await _service.GetById(quote.Id.ToString("D"));

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
        var result = (await _service.GetAll(1, 1)).Items[0];
        var byId   = await _service.GetById(result.Id);

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

        var result = await _service.GetById(quote.Id.ToString("D"));

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

        var seriesRepo = new SqliteRestorableRepository<SeriesEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        await seriesRepo.SoftDeleteAsync(series.Id);

        var result = await _service.GetById(quote.Id.ToString("D"));

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

        var result = await _service.GetById(lowercaseId.ToUpperInvariant());

        Assert.IsNotNull(result,
            "GET /quotes/{id} must resolve regardless of URL casing (#210) — the previously fully-unmitigated gap.");
    }

    [TestMethod]
    public async Task GetAll_SeriesFilter_ReturnsOnlyThatSeriesQuotes()
    {
        var series      = await InsertSeriesAsync("The Filtered Series");
        var source      = await InsertSourceAsync("A Film In The Filtered Series", series.Id);
        var quote       = await InsertQuoteAsync(source.Id, "The only quote that should match.");

        var result = await _service.GetAll(1, 10, seriesId: series.Id);

        Assert.HasCount(1, result.Items);
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

        var result = await _service.GetAll(1, 10, universeId: universe.Id);

        var ids = result.Items.Select(i => i.Id).ToList();
        Assert.Contains(quoteA.Id.ToString("D"), ids);
        Assert.Contains(quoteB.Id.ToString("D"), ids);
    }

    /// <summary>
    /// #244: found live via an IDE0060 "unused parameter" review — <c>BuildFilterWhere</c> always
    /// bound its <c>@lang</c> SQL parameter to <c>null</c> regardless of the caller-supplied <c>lang</c>
    /// value, so <c>GetAll</c>'s translation JOIN could never match. Unlike
    /// <see cref="SqliteQuoteService.GetById"/> (binds <c>lang</c> directly) or
    /// <see cref="SqliteQuoteService.GetRandom"/> (does its own per-row translation lookup after the
    /// bulk fetch), <c>GetAll</c> has no other path to translated content — this was a silent,
    /// unconditional no-op for every <c>?lang=</c> caller.
    /// </summary>
    [TestMethod]
    public async Task GetAll_LangRequested_ReturnsTranslatedContent()
    {
        var source = await InsertSourceAsync("A Film With A Dutch Translation");
        var quote  = await InsertQuoteAsync(source.Id, "Original English text.");
        await InsertQuoteTranslationAsync(quote.Id, "nl", "Nederlandse tekst.");

        var result = await _service.GetAll(1, 10, lang: "nl");

        var item = result.Items.Single(i => i.Id == quote.Id.ToString("D"));
        Assert.AreEqual("Nederlandse tekst.", item.Quote);
        Assert.AreEqual("nl", item.Language);
        Assert.IsTrue(item.IsTranslated);
    }

    [TestMethod]
    public async Task GetRandom_SeriesFilter_ReturnsOnlyThatSeriesQuotes()
    {
        var series = await InsertSeriesAsync("The Random-Filtered Series");
        var source = await InsertSourceAsync("A Film In The Random-Filtered Series", series.Id);
        var quote  = await InsertQuoteAsync(source.Id, "The only quote random selection can pick.");

        var result = await _service.GetRandom(10, seriesId: series.Id);

        Assert.HasCount(1, result.Items);
        Assert.AreEqual(quote.Id.ToString("D"), result.Items[0].Id);
    }

    [TestMethod]
    public async Task GetRandom_UniverseFilter_ReturnsOnlyThatUniverseQuotes()
    {
        var universe = await InsertUniverseAsync("The Random-Filtered Universe");
        var series   = await InsertSeriesAsync("A Series In It", universe.Id);
        var source   = await InsertSourceAsync("A Film In It", series.Id);
        var quote    = await InsertQuoteAsync(source.Id, "The only quote random selection can pick.");

        var result = await _service.GetRandom(10, universeId: universe.Id);

        Assert.HasCount(1, result.Items);
        Assert.AreEqual(quote.Id.ToString("D"), result.Items[0].Id);
    }
}
