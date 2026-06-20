using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class SqliteRepositoryTransactionTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private SqliteRepository<Widget> _repository = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        SqlMapper.AddTypeHandler(new GuidHandler());
        SqlMapper.AddTypeHandler(new SafeDateHandler());
    }

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_tx_test_").FullName;
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

        _repository = new SqliteRepository<Widget>(new SqliteConnectionFactory(_dbPath));
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
    public async Task SqliteUnitOfWork_ImplementsIUnitOfWork()
    {
        await using var uow = new SqliteUnitOfWork(new SqliteConnectionFactory(_dbPath));
        Assert.IsInstanceOfType<IUnitOfWork>(uow);
    }

    [TestMethod]
    public void IUnitOfWork_HasNoDapperTypesOnPublicInterface()
    {
        var dapperAssembly  = typeof(SqlMapper).Assembly;
        var sqliteAssembly  = typeof(SqliteConnection).Assembly;
        var systemDataAssembly = typeof(System.Data.IDbConnection).Assembly;

        var violations = typeof(IUnitOfWork)
            .GetMethods()
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType)
                .Append(m.ReturnType))
            .Where(t => t.Assembly == dapperAssembly
                     || t.Assembly == sqliteAssembly
                     || t.Assembly == systemDataAssembly)
            .Select(t => t.FullName)
            .Distinct()
            .ToList();

        Assert.AreEqual(0, violations.Count,
            $"IUnitOfWork exposes infrastructure types: {string.Join(", ", violations)}");
    }

    // ── Commit ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task InsertAsync_WithSharedConnection_CommitPersistsRecord()
    {
        var entity = new Widget { Label = "Committed" };

        await using var uow = new SqliteUnitOfWork(new SqliteConnectionFactory(_dbPath));
        await uow.BeginTransactionAsync();
        await _repository.InsertAsync(entity, uow);
        await uow.CommitAsync();

        var result = await _repository.GetByIdAsync(entity.Id);
        Assert.IsNotNull(result);
        Assert.AreEqual("Committed", result.Label);
    }

    // ── Rollback ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task InsertAsync_WithSharedConnection_RollbackRemovesRecord()
    {
        var entity = new Widget { Label = "RolledBack" };

        await using var uow = new SqliteUnitOfWork(new SqliteConnectionFactory(_dbPath));
        await uow.BeginTransactionAsync();
        await _repository.InsertAsync(entity, uow);
        await uow.RollbackAsync();

        var result = await _repository.GetByIdAsync(entity.Id);
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task Dispose_WithoutCommit_RollsBack()
    {
        var entity = new Widget { Label = "DisposeRollback" };

        await using (var uow = new SqliteUnitOfWork(new SqliteConnectionFactory(_dbPath)))
        {
            await uow.BeginTransactionAsync();
            await _repository.InsertAsync(entity, uow);
            // no commit — dispose rolls back
        }

        var result = await _repository.GetByIdAsync(entity.Id);
        Assert.IsNull(result);
    }

    // ── Atomicity ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task MultipleInserts_WithinTransaction_AreAtomic()
    {
        var a = new Widget { Label = "A" };
        var b = new Widget { Label = "B" };

        await using var uow = new SqliteUnitOfWork(new SqliteConnectionFactory(_dbPath));
        await uow.BeginTransactionAsync();
        await _repository.InsertAsync(a, uow);
        await _repository.InsertAsync(b, uow);
        await uow.CommitAsync();

        var resultA = await _repository.GetByIdAsync(a.Id);
        var resultB = await _repository.GetByIdAsync(b.Id);
        Assert.IsNotNull(resultA);
        Assert.IsNotNull(resultB);
    }
}
