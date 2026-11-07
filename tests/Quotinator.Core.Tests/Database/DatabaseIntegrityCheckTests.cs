using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Database;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Schema;
using Quotinator.Data.Testing.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Core.Tests.Database;

/// <summary>
/// #305: the main database verifies its schema against its own migration path. A difference is a hard
/// failure that stops the content load and is never repaired; an extra object is reported; and the check
/// is part of the testing procedure, proving a fresh install and a full replay end up identical.
/// <para>
/// Every scenario runs against a real database file and the real initializer, with the sabotage applied
/// by the test, never by editing shipped data.
/// </para>
/// </summary>
[TestClass]
public class DatabaseIntegrityCheckTests
{
    private TempDirectory _tempDir = null!;
    private string _dbPath = null!;
    private string _backups = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = new TempDirectory("quotinator_305_integrity_");
        _dbPath  = Path.Combine(_tempDir.Path, "test.db");
        _backups = Path.Combine(_tempDir.Path, "backups");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        _tempDir.Dispose();
    }

    [TestMethod]
    public async Task MainDatabase_BaselineAndReplay_AreIdentical()
    {
        string replayPath = Path.Combine(_tempDir.Path, "replay.db");
        await QuotinatorTestInitializer.Create(_dbPath, _backups).InitialiseAsync();
        await QuotinatorTestInitializer.Create(replayPath, _backups).InitialiseForTestingAsync(forceIncremental: true);

        AssertIdentical(await ReadSchemaAsync(_dbPath), await ReadSchemaAsync(replayPath));
    }

    [TestMethod]
    public async Task DataOwnedSchema_BaselineAndReplay_AreIdentical()
    {
        string replayPath = Path.Combine(_tempDir.Path, "replay.db");
        await DataOnlyInitializer(_dbPath).InitialiseAsync();
        await DataOnlyInitializer(replayPath).InitialiseForTestingAsync(forceIncremental: true);

        AssertIdentical(await ReadSchemaAsync(_dbPath), await ReadSchemaAsync(replayPath));
    }

    /// <summary>The table from the live incident: missing behind a current schema version and a healthy banner.</summary>
    [TestMethod]
    public async Task MissingExpectedTable_DetectedAsSchemaInconsistency_TreatedAsHardFailure()
    {
        await QuotinatorTestInitializer.Create(_dbPath, _backups).InitialiseAsync();
        await ExecuteAsync("DROP TABLE System_Notification;");

        QuotinatorDatabaseInitializer restarted = QuotinatorTestInitializer.Create(_dbPath, _backups);
        DatabaseOperationResult result = await restarted.InitialiseAsync();

        Assert.IsFalse(result.Succeeded, "a schema that does not match its version is a hard failure");
        AssertMissingTable(restarted.LastIntegrityResult, "System_Notification");
        Assert.AreEqual(0, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'System_Notification';"),
            "the missing table was recreated: the check must never self-heal");
    }

    [TestMethod]
    public async Task Difference_ContentLoadDoesNotRunAndNothingIsWritten()
    {
        await QuotinatorTestInitializer.Create(_dbPath, _backups).InitialiseAsync();
        await ExecuteAsync("DROP TABLE System_Notification;");
        int backupsBefore = BackupFileCount();

        QuotinatorDatabaseInitializer restarted = QuotinatorTestInitializer.Create(
            _dbPath, _backups, batches: [QuotinatorTestInitializer.SimpleQuoteBatch(_tempDir.Path)]);
        DatabaseOperationResult result = await restarted.InitialiseAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, await ScalarAsync("SELECT COUNT(*) FROM Quotinator_Quote;"),
            "content was loaded into a schema known not to match its version");
        Assert.AreEqual(backupsBefore, BackupFileCount(), "the check writes nothing, so it takes no backup (ADR 025)");
    }

    /// <summary>The positive control for every scenario above: the same load into a complete database runs.</summary>
    [TestMethod]
    public async Task AllExpectedTablesPresent_PassesCleanly()
    {
        QuotinatorDatabaseInitializer db = QuotinatorTestInitializer.Create(
            _dbPath, _backups, batches: [QuotinatorTestInitializer.SimpleQuoteBatch(_tempDir.Path)]);

        DatabaseOperationResult result = await db.InitialiseAsync();

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(db.LastIntegrityResult, "the check did not run");
        Assert.IsTrue(db.LastIntegrityResult.IsIntact);
        Assert.IsEmpty(db.LastIntegrityResult.Differences, "a freshly built database has no extras either");
        Assert.AreEqual(1, await ScalarAsync("SELECT COUNT(*) FROM Quotinator_Quote;"));
    }

    /// <summary>
    /// A refused migration leaves the schema behind this build, so comparing it with the build's schema
    /// means nothing. The control: the same migration with room for its backup runs, and the check with it.
    /// </summary>
    [TestMethod]
    public async Task MigrationRefused_IntegrityNotChecked()
    {
        await QuotinatorTestInitializer.Create(_dbPath, _backups).InitialiseAsync();
        SchemaMigration pending = new()
        {
            Version     = QuotinatorMigrations.All.Count + 1,
            Sql         = "CREATE TABLE IF NOT EXISTS Test_305_PendingMigration (Id INTEGER);",
            Description = "Adds the fixture table that makes a migration pending.",
        };

        QuotinatorDatabaseInitializer refused = QuotinatorTestInitializer.Create(
            _dbPath, _backups, migrations: [.. QuotinatorMigrations.All, pending], diskSpaceProvider: new FakeDiskSpaceProvider(0));
        DatabaseOperationResult refusedResult = await refused.InitialiseAsync();

        Assert.AreEqual(BackupGuardedStep.Migration, refusedResult.RefusedStep, "precondition: the migration was refused");
        Assert.IsNull(refused.LastIntegrityResult);

        QuotinatorDatabaseInitializer allowed = QuotinatorTestInitializer.Create(
            _dbPath, _backups, migrations: [.. QuotinatorMigrations.All, pending]);
        await allowed.InitialiseAsync();

        Assert.IsNotNull(allowed.LastIntegrityResult, "control: once the migration runs, so does the check");
    }

    /// <summary>
    /// A recorded version ahead of this build means the schema's shape is unknown to it, so there is
    /// nothing meaningful to compare. The control: a Reset rebuilds at this build's version and checks it.
    /// </summary>
    [TestMethod]
    public async Task SchemaVersionOvershoot_IntegrityNotChecked()
    {
        await QuotinatorTestInitializer.Create(_dbPath, _backups).InitialiseAsync();
        int recorded = await ScalarAsync("SELECT COALESCE(MAX(Version), 0) FROM System_ConsumerSchemaVersion;");
        await ExecuteAsync(
            $"INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) VALUES ({recorded + 1}, '{DateTimeOffset.UtcNow:O}');");

        QuotinatorDatabaseInitializer overshot = QuotinatorTestInitializer.Create(_dbPath, _backups);
        await overshot.InitialiseAsync();

        Assert.IsTrue(overshot.SchemaVersionOvershootDetected, "precondition: the overshoot was detected");
        Assert.IsNull(overshot.LastIntegrityResult);

        await overshot.ResetAsync();

        Assert.IsNotNull(overshot.LastIntegrityResult, "control: a Reset rebuilds at this build's version and checks it");
    }

    /// <summary>R3: a Reset re-runs the check, so "Reset repaired the database" is measured, not assumed.</summary>
    [TestMethod]
    public async Task Reset_RerunsTheCheckBeforeClearingHealth()
    {
        await QuotinatorTestInitializer.Create(_dbPath, _backups).InitialiseAsync();
        await ExecuteAsync("DROP TABLE System_Notification;");

        QuotinatorDatabaseInitializer restarted = QuotinatorTestInitializer.Create(_dbPath, _backups);
        await restarted.InitialiseAsync();
        AssertMissingTable(restarted.LastIntegrityResult, "System_Notification");

        DatabaseOperationResult reset = await restarted.ResetAsync();

        Assert.IsTrue(reset.Succeeded);
        Assert.IsNotNull(restarted.LastIntegrityResult);
        Assert.IsTrue(restarted.LastIntegrityResult.IsIntact, "the rebuilt database was not checked, or is not intact");
    }

    private static void AssertIdentical(SchemaSnapshot baseline, SchemaSnapshot replay)
    {
        Assert.IsNotEmpty(baseline.Tables, "the baseline snapshot is empty, so this comparison could not fail");
        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(baseline, replay);
        Assert.IsEmpty(result.Differences,
            "baseline and replay differ: " + string.Join("; ", result.Differences.Select(d => $"{d.Kind} {d.ObjectKind} {d.ObjectName} {d.Aspect}")));
    }

    private static void AssertMissingTable(SchemaIntegrityResult? result, string table)
    {
        Assert.IsNotNull(result, "the check did not run");
        Assert.IsFalse(result.IsIntact);
        Assert.Contains(
            d => d.Kind == SchemaDifferenceKind.Missing && d.ObjectKind == SchemaObjectKind.Table && d.ObjectName == table,
            result.Differences, $"{table} was not reported missing");
    }

    private DatabaseInitializer DataOnlyInitializer(string dbPath) => new(
        new SqliteConnectionFactory(dbPath),
        new DatabaseOptions { DbPath = dbPath, BackupsPath = _backups },
        migrations: [],
        NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
        NullLogger<DatabaseInitializer>.Instance,
        NoOpDiskSpaceProvider.Instance);

    private async Task<SchemaSnapshot> ReadSchemaAsync(string dbPath)
    {
        using SqliteConnection connection = new($"Data Source={dbPath}");
        await connection.OpenAsync(TestContext.CancellationToken);
        return await new SqliteSchemaSnapshotReader().ReadAsync(connection);
    }

    private async Task ExecuteAsync(string sql)
    {
        using (SqliteConnection connection = new($"Data Source={_dbPath}"))
        {
            await connection.OpenAsync(TestContext.CancellationToken);
            await connection.ExecuteAsync(sql);
        }

        SqliteConnection.ClearAllPools();
    }

    private async Task<int> ScalarAsync(string sql)
    {
        using SqliteConnection connection = new($"Data Source={_dbPath}");
        await connection.OpenAsync(TestContext.CancellationToken);
        return await connection.ExecuteScalarAsync<int>(sql);
    }

    private int BackupFileCount() => Directory.Exists(_backups) ? Directory.GetFiles(_backups, "*.db").Length : 0;

    public TestContext TestContext { get; set; }
}
