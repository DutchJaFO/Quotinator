using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class SourceFileOverrideRegistryTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private SourceFileOverrideRegistry _registry = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_sourcefileoverride_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS Import_SourceFileOverride (
                Id            TEXT    NOT NULL PRIMARY KEY,
                FileName      TEXT    NOT NULL,
                Origin        TEXT    NOT NULL
                              CHECK (Origin IN ('Bundled', 'UserImports')),
                ContentHash   TEXT    NOT NULL,
                SourceBatchId TEXT,
                DateCreated   TEXT    NOT NULL,
                DateModified  TEXT,
                DateDeleted   TEXT,
                IsDeleted     INTEGER NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX IF NOT EXISTS UX_Import_SourceFileOverride_FileName_Origin
                ON Import_SourceFileOverride (FileName, Origin) WHERE IsDeleted = 0;
            """);

        _registry = new SourceFileOverrideRegistry(new SqliteConnectionFactory(_dbPath));
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task FindAsync_NoRegistration_ReturnsNull()
        => Assert.IsNull(await _registry.FindAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, TestContext.CancellationToken));

    [TestMethod]
    public async Task RegisterAsync_NewFile_CanBeFoundAfterward()
    {
        await _registry.RegisterAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, "abc123", sourceBatchId: "b1", TestContext.CancellationToken);

        var found = await _registry.FindAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, TestContext.CancellationToken);

        Assert.IsNotNull(found);
        Assert.AreEqual("vilaboim-conflict-rules.json", found!.FileName);
        Assert.AreEqual(SeedBatchOrigin.Bundled, found.Origin.Parsed);
        Assert.AreEqual("abc123", found.ContentHash);
        Assert.AreEqual("b1", found.SourceBatchId);
    }

    [TestMethod]
    public async Task RegisterAsync_SameFileTwice_UpdatesExistingRowRatherThanDuplicating()
    {
        await _registry.RegisterAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, "hash-v1", sourceBatchId: "b1", TestContext.CancellationToken);
        var firstId = (await _registry.FindAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, TestContext.CancellationToken))!.Id;

        await _registry.RegisterAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, "hash-v2", sourceBatchId: "b2", TestContext.CancellationToken);
        var second = await _registry.FindAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, TestContext.CancellationToken);

        Assert.AreEqual(firstId, second!.Id, "A re-registration must update the same row, not insert a second one");
        Assert.AreEqual("hash-v2", second.ContentHash);
        Assert.AreEqual("b2", second.SourceBatchId);
    }

    [TestMethod]
    public async Task RegisterAsync_SameFileNameDifferentOrigin_TracksIndependently()
    {
        await _registry.RegisterAsync("shared-name.json", SeedBatchOrigin.Bundled, "bundled-hash", sourceBatchId: null, TestContext.CancellationToken);
        await _registry.RegisterAsync("shared-name.json", SeedBatchOrigin.UserImports, "userimports-hash", sourceBatchId: null, TestContext.CancellationToken);

        var bundled = await _registry.FindAsync("shared-name.json", SeedBatchOrigin.Bundled, TestContext.CancellationToken);
        var imports = await _registry.FindAsync("shared-name.json", SeedBatchOrigin.UserImports, TestContext.CancellationToken);

        Assert.AreEqual("bundled-hash", bundled!.ContentHash);
        Assert.AreEqual("userimports-hash", imports!.ContentHash);
    }

    [TestMethod]
    public async Task RemoveAsync_ExistingRegistration_ReturnsTrueAndSoftDeletes()
    {
        await _registry.RegisterAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, "abc123", sourceBatchId: null, TestContext.CancellationToken);

        var removed = await _registry.RemoveAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, TestContext.CancellationToken);

        Assert.IsTrue(removed);
        Assert.IsNull(await _registry.FindAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, TestContext.CancellationToken), "A soft-deleted registration must no longer be found");
    }

    [TestMethod]
    public async Task RemoveAsync_NoRegistration_ReturnsFalse()
        => Assert.IsFalse(await _registry.RemoveAsync("never-registered.json", SeedBatchOrigin.Bundled, TestContext.CancellationToken));

    [TestMethod]
    public async Task RegisterAsync_AfterRemoval_CanBeReRegistered()
    {
        await _registry.RegisterAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, "hash-v1", sourceBatchId: null, TestContext.CancellationToken);
        await _registry.RemoveAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, TestContext.CancellationToken);

        await _registry.RegisterAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, "hash-v2", sourceBatchId: null, TestContext.CancellationToken);

        var found = await _registry.FindAsync("vilaboim-conflict-rules.json", SeedBatchOrigin.Bundled, TestContext.CancellationToken);
        Assert.IsNotNull(found, "Re-registering after a soft-deleted prior row must not be blocked by the partial unique index");
        Assert.AreEqual("hash-v2", found!.ContentHash);
    }

    public TestContext TestContext { get; set; }
}
