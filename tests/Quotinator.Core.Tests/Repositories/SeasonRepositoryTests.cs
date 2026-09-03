using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Core.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Core.Tests.Repositories;

/// <summary>
/// #375: Season has no reader of its own — <c>IListableRepository&lt;SeasonEntity&gt;</c> resolves to
/// the shared generic <see cref="SqliteRepository{TEntity}"/>, exactly as Series and Universe already
/// do. <c>SqliteRepositoryTests</c> already proves that generic's <c>pageSize = 0</c> contract against
/// a synthetic table; this test proves the one thing that generic coverage cannot — that Season's own
/// real schema (<c>[Table("Quotinator_Season")]</c>, its RecordBase columns, Number/Title/Subtitle)
/// actually round-trips through it, against the real <c>Quotinator_Season</c> table.
/// </summary>
[TestClass]
public class SeasonRepositoryTests
{
    private string _tempDir = null!;
    private string _dbPath = null!;
    private SqliteRepository<SeasonEntity> _repository = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_season_repo_test_").FullName;
        _dbPath = Path.Combine(_tempDir, "test.db");

        using SqliteConnection conn = new($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE Quotinator_Season (
                Id                 TEXT    PRIMARY KEY,
                Number             INTEGER NOT NULL,
                Title              TEXT,
                Subtitle           TEXT,
                SeriesId           TEXT,
                ImportBatchId      TEXT,
                CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete',
                NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
                DateCreated        TEXT,
                DateModified       TEXT,
                DateDeleted        TEXT,
                IsDeleted          INTEGER NOT NULL DEFAULT 0
            );
            """);

        _repository = new SqliteRepository<SeasonEntity>(
            new SqliteConnectionFactory(_dbPath), NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>The repository-level control CLAUDE.md's pagination contract asks for: an endpoint
    /// test against a stub cannot catch a reader translating <c>pageSize = 0</c> into a literal SQL
    /// <c>LIMIT 0</c> — here there is no bespoke reader, so the assertion is that the shared generic
    /// still behaves correctly against Season's real table.</summary>
    [TestMethod]
    public async Task GetPageAsync_PageSizeZero_ReturnsEveryRowFromTheRealTable()
    {
        SafeValue<CompletenessStatus?> incomplete = new(CompletenessStatus.Incomplete.ToString(), CompletenessStatus.Incomplete);
        SeasonEntity book1 = new() { Id = Guid.NewGuid(), Number = 1, Title = "Book One", Subtitle = "Water", CompletenessStatus = incomplete };
        SeasonEntity book2 = new() { Id = Guid.NewGuid(), Number = 2, Title = "Book Two", Subtitle = "Earth", CompletenessStatus = incomplete };
        SeasonEntity book3 = new() { Id = Guid.NewGuid(), Number = 3, Title = "Book Three", Subtitle = "Fire", CompletenessStatus = incomplete };
        await _repository.InsertManyAsync([book1, book2, book3]);

        PagedItems<SeasonEntity> page = await _repository.GetPageAsync(1, 0);

        Assert.HasCount(3, page.Items, "pageSize=0 must return every row, not a literal LIMIT 0 empty result.");
        Assert.AreEqual(3, page.PageSize, "pageSize=0 must report the effective count actually returned.");
        Assert.Contains(s => s.Number == 1 && s.Title == "Book One" && s.Subtitle == "Water", page.Items);
        Assert.Contains(s => s.Number == 3 && s.Title == "Book Three" && s.Subtitle == "Fire", page.Items);
    }
}
