using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Helpers;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class SqliteRestorableRepositoryTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private SqliteRestorableRepository<Widget> _repository = null!;

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

        _repository = new SqliteRestorableRepository<Widget>(new SqliteConnectionFactory(_dbPath), NoOpAuditWriter.Instance, NoOpCallerContext.Instance);
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
    public void SqliteRestorableRepository_ImplementsIRestorableRepository()
        => Assert.IsInstanceOfType<IRestorableRepository<Widget>>(_repository);

    // ── GetDeletedAsync ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetDeletedAsync_ReturnsEmpty_WhenNoRecordsDeleted()
    {
        var entity = new Widget { Label = "Active" };
        await _repository.InsertAsync(entity);

        var deleted = await _repository.GetDeletedAsync();

        Assert.AreEqual(0, deleted.Count);
    }

    [TestMethod]
    public async Task GetDeletedAsync_ReturnsOnlySoftDeletedRecords()
    {
        var active  = new Widget { Label = "Active" };
        var deleted = new Widget { Label = "Deleted" };
        await _repository.InsertAsync(active);
        await _repository.InsertAsync(deleted);
        await _repository.SoftDeleteAsync(deleted.Id);

        var result = await _repository.GetDeletedAsync();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(deleted.Id, result[0].Id);
    }

    [TestMethod]
    public async Task GetDeletedAsync_ReturnsMultiple_WhenSeveralDeleted()
    {
        for (var i = 0; i < 3; i++)
        {
            var entity = new Widget { Label = $"Item {i}" };
            await _repository.InsertAsync(entity);
            await _repository.SoftDeleteAsync(entity.Id);
        }

        var result = await _repository.GetDeletedAsync();

        Assert.AreEqual(3, result.Count);
        Assert.IsTrue(result.All(r => r.IsDeleted));
    }

    // ── RestoreAsync ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RestoreAsync_MakesRecordVisibleViaGetById()
    {
        var entity = new Widget { Label = "ToRestore" };
        await _repository.InsertAsync(entity);
        await _repository.SoftDeleteAsync(entity.Id);

        await _repository.RestoreAsync(entity.Id);

        var result = await _repository.GetByIdAsync(entity.Id);
        Assert.IsNotNull(result);
        Assert.IsFalse(result.IsDeleted);
    }

    [TestMethod]
    public async Task RestoreAsync_ClearsDateDeletedAndUpdatesDateModified()
    {
        var entity = new Widget { Label = "ToRestore" };
        await _repository.InsertAsync(entity);
        await _repository.SoftDeleteAsync(entity.Id);

        await _repository.RestoreAsync(entity.Id);

        var deleted = await _repository.GetDeletedAsync();
        var active  = await _repository.GetByIdAsync(entity.Id);

        Assert.AreEqual(0, deleted.Count);
        Assert.IsNotNull(active);
        Assert.IsFalse(active.DateDeleted.IsValid);
        Assert.IsTrue(active.DateModified.IsValid);
    }

    [TestMethod]
    public async Task RestoreAsync_RemovesRecordFromGetDeleted()
    {
        var entity = new Widget { Label = "ToRestore" };
        await _repository.InsertAsync(entity);
        await _repository.SoftDeleteAsync(entity.Id);
        Assert.AreEqual(1, (await _repository.GetDeletedAsync()).Count);

        await _repository.RestoreAsync(entity.Id);

        Assert.AreEqual(0, (await _repository.GetDeletedAsync()).Count);
    }

    [TestMethod]
    public async Task RestoreAsync_NoOp_WhenRecordIsAlreadyActive()
    {
        var entity = new Widget { Label = "AlreadyActive" };
        await _repository.InsertAsync(entity);

        await _repository.RestoreAsync(entity.Id);

        var result = await _repository.GetByIdAsync(entity.Id);
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task RestoreAsync_NoOp_WhenNotFound()
    {
        await _repository.RestoreAsync(Guid.NewGuid());
    }

    // ── HardDeleteAsync ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task HardDeleteAsync_RemovesSoftDeletedRecord()
    {
        var entity = new Widget { Label = "ToHardDelete" };
        await _repository.InsertAsync(entity);
        await _repository.SoftDeleteAsync(entity.Id);

        await _repository.HardDeleteAsync(entity.Id);

        Assert.AreEqual(0, (await _repository.GetDeletedAsync()).Count);
    }

    [TestMethod]
    public async Task HardDeleteAsync_DoesNotRemoveActiveRecord()
    {
        var entity = new Widget { Label = "Active" };
        await _repository.InsertAsync(entity);

        await _repository.HardDeleteAsync(entity.Id);

        var result = await _repository.GetByIdAsync(entity.Id);
        Assert.IsNotNull(result, "HardDeleteAsync must not remove active records");
    }

    [TestMethod]
    public async Task HardDeleteAsync_NoOp_WhenNotFound()
    {
        await _repository.HardDeleteAsync(Guid.NewGuid());
    }

    // ── PurgeAsync ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PurgeAsync_RemovesAllSoftDeletedRecords()
    {
        for (var i = 0; i < 3; i++)
        {
            var entity = new Widget { Label = $"ToDelete {i}" };
            await _repository.InsertAsync(entity);
            await _repository.SoftDeleteAsync(entity.Id);
        }

        await _repository.PurgeAsync();

        Assert.AreEqual(0, (await _repository.GetDeletedAsync()).Count);
    }

    [TestMethod]
    public async Task PurgeAsync_ReturnsPurgedCount()
    {
        for (var i = 0; i < 4; i++)
        {
            var entity = new Widget { Label = $"ToDelete {i}" };
            await _repository.InsertAsync(entity);
            await _repository.SoftDeleteAsync(entity.Id);
        }

        var count = await _repository.PurgeAsync();

        Assert.AreEqual(4, count);
    }

    [TestMethod]
    public async Task PurgeAsync_PreservesActiveRecords()
    {
        var active  = new Widget { Label = "Active" };
        var deleted = new Widget { Label = "Deleted" };
        await _repository.InsertAsync(active);
        await _repository.InsertAsync(deleted);
        await _repository.SoftDeleteAsync(deleted.Id);

        await _repository.PurgeAsync();

        Assert.IsNotNull(await _repository.GetByIdAsync(active.Id));
    }

    [TestMethod]
    public async Task PurgeAsync_Returns0_WhenNothingToDelete()
    {
        var entity = new Widget { Label = "Active" };
        await _repository.InsertAsync(entity);

        var count = await _repository.PurgeAsync();

        Assert.AreEqual(0, count);
    }
}
