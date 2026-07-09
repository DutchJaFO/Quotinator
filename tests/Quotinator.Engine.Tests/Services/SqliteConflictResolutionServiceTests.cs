using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Import;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Database;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Models;
using Quotinator.Engine.Repositories;
using Quotinator.Engine.Services;

namespace Quotinator.Engine.Tests.Services;

/// <summary>
/// Integration tests for <see cref="SqliteConflictResolutionService"/> (#149) — the one Quotinator-
/// specific piece of the manual conflict-review workflow (everything else is generically tested in
/// <c>Quotinator.Data.Tests</c> against a fake apply callback). Exercises the real
/// decide → apply sequence against a genuine pending conflict produced by the seeding pipeline under
/// <see cref="DuplicateResolutionPolicy.Review"/>, mirroring <see cref="Database.ConflictResolutionTests"/>'s
/// fixture files exactly.
/// </summary>
[TestClass]
public class SqliteConflictResolutionServiceTests
{
    private const string SharedId = "11111111-1111-1111-1111-111111111111";

    private string _tempDir = null!;
    private string _dbPath  = null!;
    private string _backups = null!;
    private SqliteConnectionFactory _factory = null!;
    private ISystemImportConflictReader _conflictReader = null!;
    private ISystemImportConflictWriter _conflictWriter = null!;
    private IImportBatchRepository _importBatches = null!;
    private SqliteConflictResolutionService _service = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_conflict_svc_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
        _backups = Path.Combine(_tempDir, "backups");
        _factory = new SqliteConnectionFactory(_dbPath);

        _conflictReader = new SystemImportConflictReader(_factory);
        _conflictWriter = new SystemImportConflictWriter(_factory);
        _importBatches  = new SqliteImportBatchRepository(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);

        var coordinator  = new ConflictResolutionCoordinator(_conflictReader, _conflictWriter, _factory);
        var changeLogWriter = new SystemChangeLogWriter(_factory);
        _service = new SqliteConflictResolutionService(_conflictReader, coordinator, _importBatches, changeLogWriter);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteQuoteFile(string name, string json)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, json);
        return path;
    }

    // Existing side: no character, "Original quote text", genre "drama".
    private string WriteFirstFile() => WriteQuoteFile("first.json", $$"""
        [{"id":"{{SharedId}}","quote":"Original quote text","source":"Same Source","date":"1990","type":"movie","genres":["drama"]}]
        """);

    // Incoming side: adds a character (existing side blank — auto-resolves), differs on quote text
    // and genres (both non-empty on both sides — genuinely ambiguous, needs an explicit decision).
    private string WriteSecondFile() => WriteQuoteFile("second.json", $$"""
        [{"id":"{{SharedId}}","quote":"Updated quote text","source":"Same Source","date":"1990","character":"Neo","type":"movie","genres":["comedy"]}]
        """);

    /// <summary>
    /// Seeds the existing (first) row via the real engine, then manufactures the pending
    /// <c>System_ImportConflicts</c> row directly for the second (incoming/duplicate) file — seeding
    /// itself no longer writes there (#154 superseded seeding's conflict logging with
    /// <c>System_ImportActions</c>), but <see cref="SqliteConflictResolutionService"/> (this test's
    /// subject) is untouched and still needs a real pending row to decide/apply against.
    /// </summary>
    private async Task<Guid> SeedPendingConflictAsync()
    {
        var firstPath  = WriteFirstFile();
        var secondPath = WriteSecondFile();

        var actionReader  = new SystemImportActionReader(_factory);
        var actionWriter  = new SystemImportActionWriter(_factory);
        var coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, _factory);
        var actionService = new SqliteImportActionService(actionReader, coordinator, NoOpSystemChangeLogWriter.Instance);

        var firstBatch = new SeedBatch([new SeedFile(firstPath, null)], new ManifestPolicy(DuplicateResolutionPolicy.NewestWins), "test");
        var options    = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var db = new QuotinatorDatabaseInitializer(
            _factory, options, QuotinatorMigrations.All, [firstBatch], _importBatches,
            coordinator, actionService,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance,
            NoOpSourceCacheUpdater.Instance, autoUpdateSources: false, QuotinatorMigrations.Baseline);
        await db.InitialiseAsync();

        SourceQuoteFileReader.TryParse(File.ReadAllText(firstPath),  out var firstQuotes);
        SourceQuoteFileReader.TryParse(File.ReadAllText(secondPath), out var secondQuotes);
        var existingFields = QuoteFieldMerge.ToFieldMap(firstQuotes![0]);
        var incomingFields = QuoteFieldMerge.ToFieldMap(secondQuotes![0]);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var existingBatchId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT Id FROM ImportBatches WHERE Name = @name", new { name = Path.GetFileName(firstPath) });

        // A real ImportBatches row for the incoming (second) file — Characters.ImportBatchId is a
        // genuine FK, so the conflict's incoming batch id must reference an existing row, not a bare
        // Guid.NewGuid(), or applying the eventual decision fails with a foreign key violation when
        // GetOrCreateCharacterAsync inserts the not-yet-existing "Neo" character.
        var incomingBatch = new ImportBatch
        {
            Name           = Path.GetFileName(secondPath),
            Type           = new SafeValue<ImportBatchType?>(ImportBatchType.Import.ToString(), ImportBatchType.Import),
            ImportedAt     = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat),
            ConflictPolicy = new SafeValue<DuplicateResolutionPolicy?>(DuplicateResolutionPolicy.Review.ToString(), DuplicateResolutionPolicy.Review),
        };
        await _importBatches.InsertAsync(incomingBatch);

        await QuoteSeedWriter.LogImportConflictAsync(
            _conflictWriter, incomingBatch.Id, SharedId, DuplicateResolutionPolicy.Review,
            existingFields, incomingFields, mergeResult: null, conn,
            existingBatchId: existingBatchId.ToString("D").ToUpperInvariant());

        var page = await _conflictReader.GetPagedAsync(null, ImportConflictStatus.Pending.ToString(), 1, 10);
        Assert.HasCount(1, page.Items, "Fixture must produce exactly one pending conflict.");
        return page.Items[0].Id;
    }

    private async Task<(string QuoteText, string? Character, List<string> Genres)> ReadResultAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        var row = await conn.QuerySingleAsync<(string QuoteText, string? Character)>(
            "SELECT q.QuoteText, c.Name AS Character FROM Quotes q " +
            "LEFT JOIN Characters c ON c.Id = q.CharacterId " +
            "WHERE q.Id = @id", new { id = SharedId });
        var genres = (await conn.QueryAsync<string>(
            "SELECT Genre FROM QuoteGenres WHERE QuoteId = @id", new { id = SharedId })).ToList();

        return (row.QuoteText, row.Character, genres);
    }

    private async Task<int> ChangeLogCountAsync(string initiatedByType)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_ChangeLog WHERE EntityId = @id AND InitiatedByType = @type",
            new { id = SharedId, type = initiatedByType });
    }

    [TestMethod]
    public async Task GetPagedAsync_PendingConflict_ReportsOnlyGenuinelyAmbiguousFields()
    {
        await SeedPendingConflictAsync();

        var page = await _service.GetPagedAsync(null, ImportConflictStatus.Pending.ToString(), 1, 10);

        Assert.HasCount(1, page.Items);
        CollectionAssert.AreEquivalent(new[] { "quoteText", "genres" }, page.Items[0].AmbiguousFields.ToList(),
            "character auto-resolves (existing side blank) — only quoteText and genres genuinely conflict");
        Assert.IsFalse(page.Items[0].SameFile, "the two fixture files are two separate batches");
    }

    [TestMethod]
    public async Task DecideAsync_AmbiguousFieldLeftUndecided_ThrowsUnresolvedFieldConflictException()
    {
        var conflictId = await SeedPendingConflictAsync();

        // genres left undecided — genuinely ambiguous, must throw before anything is staged.
        await Assert.ThrowsExactlyAsync<UnresolvedFieldConflictException>(() => _service.DecideAsync(conflictId,
            new ConflictDecisionRequest { QuoteText = new FieldDecision { Choice = FieldResolutionChoice.Replace } }));
    }

    [TestMethod]
    public async Task DecideAsync_ThenApplyBatch_WritesResolvedFieldsAndOneChangeLogRow()
    {
        var conflictId = await SeedPendingConflictAsync();
        var page = await _service.GetPagedAsync(null, ImportConflictStatus.Pending.ToString(), 1, 10);
        var batchId = page.Items[0].BatchId;

        await _service.DecideAsync(conflictId, new ConflictDecisionRequest
        {
            QuoteText = new FieldDecision { Choice = FieldResolutionChoice.Replace },
            Genres    = new GenresFieldDecision { Choice = FieldResolutionChoice.Custom, Value = ["drama", "comedy"] },
        });

        var stillPending = await _service.ApplyBatchAsync(batchId);
        Assert.IsNull(stillPending, "Every conflict in the batch was decided — apply must succeed.");

        var (quoteText, character, genres) = await ReadResultAsync();
        Assert.AreEqual("Updated quote text", quoteText, "Replace decision must take the incoming value");
        Assert.AreEqual("Neo", character, "Existing character was blank — auto-resolved from incoming regardless of any decision");
        CollectionAssert.AreEquivalent(new[] { "Drama", "Comedy" }, genres, "Custom decision overrides both sides");

        Assert.AreEqual(1, await ChangeLogCountAsync("WriteEndpoint"),
            "Applying a decided conflict must write exactly one System_ChangeLog row with InitiatedByType=WriteEndpoint");

        var resolved = await _conflictReader.GetByIdAsync(conflictId);
        Assert.AreEqual(ImportConflictStatus.Resolved, resolved!.Status.Parsed);
    }

    [TestMethod]
    public async Task UndoDecisionAsync_BeforeApply_RevertsDecisionAndBatchStaysUnapplied()
    {
        var conflictId = await SeedPendingConflictAsync();
        var page = await _service.GetPagedAsync(null, ImportConflictStatus.Pending.ToString(), 1, 10);
        var batchId = page.Items[0].BatchId;

        await _service.DecideAsync(conflictId, new ConflictDecisionRequest
        {
            QuoteText = new FieldDecision { Choice = FieldResolutionChoice.Keep },
            Genres    = new GenresFieldDecision { Choice = FieldResolutionChoice.Keep },
        });

        await _service.UndoDecisionAsync(conflictId);

        var conflict = await _conflictReader.GetByIdAsync(conflictId);
        Assert.AreEqual(ImportConflictStatus.Pending, conflict!.Status.Parsed);

        var stillPending = await _service.ApplyBatchAsync(batchId);
        Assert.IsNotNull(stillPending, "Apply must refuse — the conflict's decision was undone.");
        CollectionAssert.AreEqual(new[] { conflictId }, stillPending!.PendingConflictIds.ToList());

        var (quoteText, _, _) = await ReadResultAsync();
        Assert.AreEqual("Original quote text", quoteText, "Nothing should have been written — apply never ran.");
    }

    [TestMethod]
    public async Task GetPagedAsync_StatusFilterLowercase_StillMatchesStoredValue()
    {
        await SeedPendingConflictAsync();

        var page = await _service.GetPagedAsync(null, "pending", 1, 10);

        Assert.HasCount(1, page.Items, "A lowercase status filter must still match the uppercase-cased 'Pending' stored value.");
    }

    [TestMethod]
    public async Task ApplyBatchAsync_LowercaseBatchId_StillMatchesUppercaseStoredValue()
    {
        var conflictId = await SeedPendingConflictAsync();
        var page = await _service.GetPagedAsync(null, ImportConflictStatus.Pending.ToString(), 1, 10);
        var lowercaseBatchId = page.Items[0].BatchId.ToLowerInvariant();

        await _service.DecideAsync(conflictId, new ConflictDecisionRequest
        {
            QuoteText = new FieldDecision { Choice = FieldResolutionChoice.Keep },
            Genres    = new GenresFieldDecision { Choice = FieldResolutionChoice.Keep },
        });

        var stillPending = await _service.ApplyBatchAsync(lowercaseBatchId);

        Assert.IsNull(stillPending, "Nothing pending — apply must succeed despite the lowercase batch id.");
    }
}
