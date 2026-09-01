using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Testing.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// Proves #156's <c>SeedSystemContentAsync</c> extension point end-to-end using a test-only
/// <c>SystemContent_</c>-prefixed table — never added to Quotinator.Data's real
/// <c>DataOwnedMigrations</c>/baseline, so this never touches the shipped schema. Represents "a
/// dataset the library itself defines as a standard, vital feature." See
/// docs/milestones/maintenance-milestone-v1.8.0/156-reset-baseline-and-system-reseed-plan.md.
/// </summary>
[TestClass]
public class SystemReseedConceptTests
{
    private const string CreateTableSql =
        "CREATE TABLE IF NOT EXISTS SystemContent_ExampleSetting (Id TEXT NOT NULL PRIMARY KEY, Code TEXT NOT NULL, DateCreated TEXT NOT NULL);";

    private const string ExampleSettingId = "11111111-1111-1111-1111-111111111111";

    private sealed class SystemContentTestInitializer(
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
                "INSERT INTO SystemContent_ExampleSetting (Id, Code, DateCreated) VALUES (@id, 'example', @now) " +
                "ON CONFLICT(Id) DO NOTHING;",
                new { id = ExampleSettingId, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
        }
    }

    private static SystemContentTestInitializer CreateInitializer(string dbPath)
    {
        SqliteConnectionFactory factory = new SqliteConnectionFactory(dbPath);
        DatabaseOptions options = new DatabaseOptions
        {
            DbPath      = dbPath,
            BackupsPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups"),
        };
        IReadOnlyList<SchemaMigration> migrations =
        [
            new SchemaMigration { Version = 1, Sql = CreateTableSql },
        ];
        return new SystemContentTestInitializer(factory, options, migrations, new SchemaBaseline { Sql = CreateTableSql });
    }

    [TestMethod]
    public async Task SeedSystemContentAsync_AfterFreshInitialise_PopulatesSystemContentTable()
    {
        using TempDatabase temp = new TempDatabase([]);
        SystemContentTestInitializer db = CreateInitializer(temp.DbPath);

        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={temp.DbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        int count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SystemContent_ExampleSetting;");
        Assert.AreEqual(1, count, "System content must be seeded on first-ever install.");
    }

    [TestMethod]
    public async Task SeedSystemContentAsync_AfterReset_RepopulatesSystemContentTable()
    {
        using TempDatabase temp = new TempDatabase([]);
        SystemContentTestInitializer db = CreateInitializer(temp.DbPath);
        await db.InitialiseAsync();

        await db.ResetAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={temp.DbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        int count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SystemContent_ExampleSetting;");
        Assert.AreEqual(1, count,
            "System content must be present again after Reset — the whole point of #156's 'after any reset' requirement.");
        Assert.AreEqual(2, db.SeedSystemContentCallCount, "Hook must fire once at fresh install and once more at Reset.");
    }

    [TestMethod]
    public async Task ReseedEquivalentCall_DoesNotInvokeSeedSystemContentAsync()
    {
        using TempDatabase temp = new TempDatabase([]);
        SystemContentTestInitializer db = CreateInitializer(temp.DbPath);
        await db.InitialiseAsync();
        Assert.AreEqual(1, db.SeedSystemContentCallCount);

        await db.ReseedAsync();

        Assert.AreEqual(1, db.SeedSystemContentCallCount,
            "Standard reseed must never trigger system-content reseeding — the two actions are separate.");
    }

    public TestContext TestContext { get; set; }
}
