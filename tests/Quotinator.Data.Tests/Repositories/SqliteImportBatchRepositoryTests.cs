using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Repositories;

/// <summary>
/// #212: direct repository-level round-trip coverage for <see cref="SqliteImportBatchRepository.GetAllAsync"/>/
/// <see cref="SqliteImportBatchRepository.GetByTypeAsync"/> — neither had a dedicated test before this
/// issue rewrote the exact SQL both methods run (<see cref="Quotinator.Data.Queries.Sql.ImportBatches"/>).
/// <see cref="SqliteImportBatchRepository.GetAllAsync"/> was previously only covered indirectly, via
/// <c>SqliteImportActionService.ReverseBatchAsync</c>'s test suite in <c>Quotinator.Core.Tests</c>;
/// <see cref="SqliteImportBatchRepository.GetByTypeAsync"/> had none at all.
/// </summary>
[TestClass]
public class SqliteImportBatchRepositoryTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private SqliteImportBatchRepository _repository = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_importbatch_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE ImportBatches (
                Id             TEXT    PRIMARY KEY,
                Name           TEXT    NOT NULL,
                Type           TEXT    NOT NULL CHECK (Type IN ('Seed', 'Import', 'System', 'UserSeed')),
                Url            TEXT,
                ImportedAt     TEXT    NOT NULL,
                ImportedById   TEXT,
                RecordCount    INTEGER NOT NULL DEFAULT 0,
                DateCreated    TEXT    NOT NULL,
                DateModified   TEXT,
                DateDeleted    TEXT,
                IsDeleted      INTEGER NOT NULL DEFAULT 0,
                ConflictPolicy TEXT    NOT NULL DEFAULT 'skip',
                Status         TEXT    NOT NULL DEFAULT 'Applied'
                               CHECK (Status IN ('Staged', 'Applied', 'Discarded')),
                AppliedAt      TEXT
            );
            """);

        _repository = new SqliteImportBatchRepository(
            new SqliteConnectionFactory(_dbPath), NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static ImportBatch BuildBatch(ImportBatchType type, string name = "test-batch.json", string? importedAt = null) => new()
    {
        Name           = name,
        Type           = new SafeValue<ImportBatchType?>(type.ToString(), type),
        Url            = "https://example.test/source.json",
        ImportedAt     = importedAt ?? SafeDateValue.Now.Raw,
        ImportedById   = "11111111-1111-4111-8111-111111111111",
        RecordCount    = 42,
        ConflictPolicy = new SafeValue<DuplicateResolutionPolicy?>(DuplicateResolutionPolicy.NewestWins.ToString(), DuplicateResolutionPolicy.NewestWins),
        Status         = new SafeValue<ImportBatchStatus?>(ImportBatchStatus.Applied.ToString(), ImportBatchStatus.Applied),
        AppliedAt      = SafeDateValue.Now.Raw,
    };

    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetAllAsync_InsertedBatch_ReturnsAllPersistedFields()
    {
        var batch = BuildBatch(ImportBatchType.Import);
        await _repository.InsertAsync(batch);

        var results = await _repository.GetAllAsync();
        var result  = results.Single();

        Assert.AreEqual(batch.Id, result.Id);
        Assert.AreEqual(batch.Name, result.Name);
        Assert.AreEqual(ImportBatchType.Import, result.Type.Parsed);
        Assert.AreEqual(batch.Url, result.Url);
        Assert.AreEqual(batch.ImportedAt, result.ImportedAt);
        Assert.AreEqual(batch.ImportedById, result.ImportedById);
        Assert.AreEqual(batch.RecordCount, result.RecordCount);
        Assert.AreEqual(DuplicateResolutionPolicy.NewestWins, result.ConflictPolicy.Parsed);
        Assert.AreEqual(ImportBatchStatus.Applied, result.Status.Parsed);
        Assert.AreEqual(batch.AppliedAt, result.AppliedAt);
        Assert.IsFalse(result.IsDeleted);
    }

    [TestMethod]
    public async Task GetAllAsync_TwoBatchesSameSecond_OrdersByRowidDescendingOnTie()
    {
        // Both batches share the same ImportedAt (whole-second precision) — ROWID DESC must break the
        // tie in insertion order, per Sql.ImportBatches.SelectAll's own documented ordering comment.
        var sharedImportedAt = SafeDateValue.Now.Raw;
        var first  = BuildBatch(ImportBatchType.Seed, "first.json", sharedImportedAt);
        var second = BuildBatch(ImportBatchType.Seed, "second.json", sharedImportedAt);

        await _repository.InsertAsync(first);
        await _repository.InsertAsync(second);

        var results = await _repository.GetAllAsync();

        Assert.HasCount(2, results);
        Assert.AreEqual(second.Id, results[0].Id, "The later-inserted row must sort first under a same-second tie");
        Assert.AreEqual(first.Id, results[1].Id);
    }

    // ── #213: ImportedById mixed-case presentation ──────────────────────────────

    /// <summary>
    /// A deliberately mixed-case <c>ImportedById</c> value, written directly (bypassing the repository —
    /// no capture-time canonicalization exists for this column, since nothing in <c>src/</c> writes it
    /// today per #213's own Background), renders lowercase through <see cref="SqliteImportBatchRepository.GetAllAsync"/>
    /// (<c>Sql.ImportBatches.SelectAll</c>, the query #212 rewrote away from <c>SELECT *</c>). Mirrors
    /// <c>SystemImportActionWriterReaderTests.ExistingBatchId_RoundTripsCorrectly</c>'s pattern.
    /// </summary>
    [TestMethod]
    public async Task GetAllAsync_MixedCaseImportedById_RendersLowercase()
    {
        var id   = Guid.NewGuid().ToString();
        var now  = SafeDateValue.Now.Raw;
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            conn.Execute(
                "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, ImportedById, DateCreated) " +
                "VALUES (@id, 'mixed-case.json', 'Import', @now, UPPER('aabbccdd-1234-4abc-8def-1234567890ab'), @now);",
                new { id, now });
        }

        var results = await _repository.GetAllAsync();
        var result  = results.Single();

        Assert.AreEqual("aabbccdd-1234-4abc-8def-1234567890ab", result.ImportedById,
            "ImportedById must render lowercase regardless of the casing actually stored");
    }

    /// <summary>
    /// The same mixed-case value round-trips lowercase through the generic
    /// <see cref="SqliteRepository{T}.GetByIdAsync"/> path, proving both read paths — the hand-written
    /// <c>Sql.ImportBatches</c> queries and <c>RepositorySql</c>'s reflection-driven generic queries —
    /// present <c>ImportedById</c> consistently.
    /// </summary>
    [TestMethod]
    public async Task GetByIdAsync_MixedCaseImportedById_RendersLowercase()
    {
        var id   = Guid.NewGuid().ToString();
        var now  = SafeDateValue.Now.Raw;
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            conn.Execute(
                "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, ImportedById, DateCreated) " +
                "VALUES (@id, 'mixed-case.json', 'Import', @now, UPPER('aabbccdd-1234-4abc-8def-1234567890ab'), @now);",
                new { id, now });
        }

        var result = await _repository.GetByIdAsync(Guid.Parse(id));

        Assert.IsNotNull(result);
        Assert.AreEqual("aabbccdd-1234-4abc-8def-1234567890ab", result.ImportedById,
            "ImportedById must render lowercase through the generic repository path too");
    }

    // ── GetByTypeAsync ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetByTypeAsync_MixedTypes_ReturnsOnlyMatchingType()
    {
        var seedBatch   = BuildBatch(ImportBatchType.Seed, "seed.json");
        var importBatch = BuildBatch(ImportBatchType.Import, "import.json");
        await _repository.InsertAsync(seedBatch);
        await _repository.InsertAsync(importBatch);

        var results = await _repository.GetByTypeAsync(ImportBatchType.Seed);
        var result  = results.Single();

        Assert.AreEqual(seedBatch.Id, result.Id);
        Assert.AreEqual("seed.json", result.Name);
        Assert.AreEqual(ImportBatchType.Seed, result.Type.Parsed);
        Assert.AreEqual(seedBatch.RecordCount, result.RecordCount);
        Assert.AreEqual(ImportBatchStatus.Applied, result.Status.Parsed);
    }
}
