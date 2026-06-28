using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class SqliteRepositoryTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private SqliteRepository<Widget> _repository = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_data_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE Widgets (
                Id           TEXT    NOT NULL PRIMARY KEY,
                Label        TEXT    NOT NULL,
                DateCreated  TEXT,
                DateModified TEXT,
                DateDeleted  TEXT,
                IsDeleted    INTEGER NOT NULL DEFAULT 0
            );
            """);

        _repository = new SqliteRepository<Widget>(new SqliteConnectionFactory(_dbPath), NoOpAuditWriter.Instance, NoOpCallerContext.Instance);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Contract ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void SqliteRepository_ImplementsIRepository()
        => Assert.IsInstanceOfType<IRepository<Widget>>(_repository);

    // ── Insert + GetById ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task InsertAsync_ThenGetById_ReturnsRecord()
    {
        var entity = new Widget { Label = "Hello" };

        await _repository.InsertAsync(entity);
        var result = await _repository.GetByIdAsync(entity.Id);

        Assert.IsNotNull(result);
        Assert.AreEqual(entity.Id, result.Id);
        Assert.AreEqual("Hello", result.Label);
        Assert.IsFalse(result.IsDeleted);
    }

    [TestMethod]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _repository.GetByIdAsync(Guid.NewGuid());
        Assert.IsNull(result);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task UpdateAsync_PersistsChanges()
    {
        var entity = new Widget { Label = "Before" };
        await _repository.InsertAsync(entity);

        entity.Label = "After";
        await _repository.UpdateAsync(entity);
        var result = await _repository.GetByIdAsync(entity.Id);

        Assert.IsNotNull(result);
        Assert.AreEqual("After", result.Label);
        Assert.IsTrue(result.DateModified.IsValid);
    }

    // ── SoftDelete ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SoftDeleteAsync_HidesRecordFromGetById()
    {
        var entity = new Widget { Label = "ToDelete" };
        await _repository.InsertAsync(entity);

        await _repository.SoftDeleteAsync(entity.Id);

        Assert.IsNull(await _repository.GetByIdAsync(entity.Id));
    }

    [TestMethod]
    public async Task SoftDeleteAsync_SetsAuditColumns()
    {
        var entity = new Widget { Label = "ToDelete" };
        await _repository.InsertAsync(entity);

        await _repository.SoftDeleteAsync(entity.Id);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var raw = conn.QuerySingleOrDefault<Widget>(
            "SELECT * FROM Widgets WHERE Id = @id", new { id = entity.Id.ToString("D").ToUpperInvariant() });

        Assert.IsNotNull(raw);
        Assert.IsTrue(raw.IsDeleted);
        Assert.IsTrue(raw.DateDeleted.IsValid);
        Assert.IsTrue(raw.DateModified.IsValid);
    }

    [TestMethod]
    public async Task SoftDeleteAsync_IsIdempotent()
    {
        var entity = new Widget { Label = "Idempotent" };
        await _repository.InsertAsync(entity);

        await _repository.SoftDeleteAsync(entity.Id);
        await _repository.SoftDeleteAsync(entity.Id);
    }

    [TestMethod]
    public async Task SoftDeleteAsync_NoOp_WhenEntityNotFound()
    {
        await _repository.SoftDeleteAsync(Guid.NewGuid());
    }


}
