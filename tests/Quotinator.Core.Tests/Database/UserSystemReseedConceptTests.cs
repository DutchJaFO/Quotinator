using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Core.Tests.Database;

/// <summary>
/// Proves #156's <c>SeedSystemContentAsync</c> extension point works from a downstream consumer's
/// side too, not just Quotinator.Data's own — Quotinator.Core is, architecturally, itself just a
/// consumer/"user" of Quotinator.Data (see ADR 004/015), so this test suite stands in for "a dataset
/// a user of the library might define." Uses a test-only <c>UserContent_</c>-prefixed table, never
/// added to <c>QuotinatorMigrations.All</c>/<c>QuotinatorMigrations.Baseline</c> — this never touches
/// the shipped schema. See
/// docs/milestones/maintenance-milestone-v1.8.0/156-reset-baseline-and-system-reseed-plan.md.
/// </summary>
[TestClass]
public class UserSystemReseedConceptTests
{
    private const string CreateTableSql =
        "CREATE TABLE IF NOT EXISTS UserContent_ExampleWidget (Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, DateCreated TEXT NOT NULL);";

    private const string ExampleWidgetId = "22222222-2222-2222-2222-222222222222";

    private sealed class UserContentTestInitializer(
        IDbConnectionFactory factory, DatabaseOptions options, IReadOnlyList<SchemaMigration> migrations, SchemaBaseline baseline) : DatabaseInitializer(factory, options, migrations, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance, NoOpDiskSpaceProvider.Instance, baseline)
    {
        public int SeedSystemContentCallCount { get; private set; }

        protected override Task OnResetAsync(SqliteConnection connection, bool preserveSchemaVersion, bool forceSourceRefresh)
            => DropAndRebuildAsync(connection, preserveSchemaVersion);

        // Standard-reseed stand-in: deliberately does NOT call SeedSystemContentAsync, proving the
        // two reseed actions stay separate — a standard reseed never touches system content.
        protected override Task OnReseedAsync(SqliteConnection connection, bool forceSourceRefresh) => Task.CompletedTask;

        protected override async Task SeedSystemContentAsync(SqliteConnection connection)
        {
            SeedSystemContentCallCount++;
            await connection.ExecuteAsync(
                "INSERT INTO UserContent_ExampleWidget (Id, Name, DateCreated) VALUES (@id, 'example', @now) " +
                "ON CONFLICT(Id) DO NOTHING;",
                new { id = ExampleWidgetId, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
        }
    }

    private string _tempDir = null!;
    private string _dbPath  = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private UserContentTestInitializer CreateInitializer()
    {
        SqliteConnectionFactory factory = new SqliteConnectionFactory(_dbPath);
        DatabaseOptions options = new DatabaseOptions
        {
            DbPath      = _dbPath,
            BackupsPath = Path.Combine(_tempDir, "backups"),
        };
        IReadOnlyList<SchemaMigration> migrations =
        [
            new SchemaMigration { Version = 1, Sql = CreateTableSql },
        ];
        return new UserContentTestInitializer(factory, options, migrations, new SchemaBaseline { Sql = CreateTableSql });
    }

    [TestMethod]
    public async Task SeedSystemContentAsync_AfterFreshInitialise_PopulatesUserContentTable()
    {
        UserContentTestInitializer db = CreateInitializer();

        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        int count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM UserContent_ExampleWidget;");
        Assert.AreEqual(1, count, "A consumer-defined system table must be seeded on first-ever install, via the same extension point Quotinator.Data itself uses.");
    }

    [TestMethod]
    public async Task SeedSystemContentAsync_AfterReset_RepopulatesUserContentTable()
    {
        UserContentTestInitializer db = CreateInitializer();
        await db.InitialiseAsync();

        await db.ResetAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        int count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM UserContent_ExampleWidget;");
        Assert.AreEqual(1, count, "A consumer-defined system table's content must be present again after Reset.");
        Assert.AreEqual(2, db.SeedSystemContentCallCount, "Hook must fire once at fresh install and once more at Reset.");
    }

    [TestMethod]
    public async Task ReseedEquivalentCall_DoesNotInvokeSeedSystemContentAsync()
    {
        UserContentTestInitializer db = CreateInitializer();
        await db.InitialiseAsync();
        Assert.AreEqual(1, db.SeedSystemContentCallCount);

        await db.ReseedAsync();

        Assert.AreEqual(1, db.SeedSystemContentCallCount, "Standard reseed must never trigger system-content reseeding for a consumer's own content either.");
    }

    public TestContext TestContext { get; set; }
}
