using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Testing.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// Proves that a seeding step which fails after it has written something leaves the database as it was
/// before the attempt: <c>RunInitialisedHookAsync</c> takes a pre-seed backup and restores it when the
/// seeding hook throws. The only other evidence of that restore was a log line, which
/// <c>startup-and-degradation/01</c> used to assert by its wording.
/// </summary>
[TestClass]
public class DatabaseInitializerSeedRestoreTests
{
    private const string MarkerTable = "SeedRestoreTest_Marker";

    /// <summary>Test context, for cancellation.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A seeding hook that writes a marker table, and then throws when <paramref name="failAfterWriting"/> is set.</summary>
    /// <param name="factory">Factory used to open SQLite connections.</param>
    /// <param name="options">Database file paths.</param>
    /// <param name="failAfterWriting">Whether the hook throws after its write.</param>
    private sealed class WritingSeedInitializer(IDbConnectionFactory factory, DatabaseOptions options, bool failAfterWriting)
        : DatabaseInitializer(factory, options, [], NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance, NoOpDiskSpaceProvider.Instance)
    {
        protected override async Task OnInitialisedAsync(SqliteConnection connection, bool databaseWasCreated)
        {
            if (databaseWasCreated)
            {
                return;
            }

            await connection.ExecuteAsync($"CREATE TABLE {MarkerTable} (Id INTEGER NOT NULL);");

            if (failAfterWriting)
            {
                throw new InvalidOperationException("Seeding failed after writing, as this test arranges.");
            }
        }
    }

    [TestMethod]
    public async Task InitialiseAsync_SeedingFailsAfterWriting_RestoresThePreSeedDatabase()
    {
        using TempDatabase temp = new([]);
        await CreateInitializer(temp.DbPath, failAfterWriting: false).InitialiseAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => CreateInitializer(temp.DbPath, failAfterWriting: true).InitialiseAsync());

        Assert.IsFalse(await MarkerTableExistsAsync(temp.DbPath),
            "The pre-seed backup must be restored, so what the failed seeding wrote is gone");
    }

    [TestMethod]
    public async Task InitialiseAsync_SeedingSucceeds_KeepsWhatItWrote()
    {
        using TempDatabase temp = new([]);
        await CreateInitializer(temp.DbPath, failAfterWriting: false).InitialiseAsync();

        await CreateInitializer(temp.DbPath, failAfterWriting: false).InitialiseAsync();

        Assert.IsTrue(await MarkerTableExistsAsync(temp.DbPath),
            "A seeding hook that succeeds keeps its write: the positive control for the restore test");
    }

    private static WritingSeedInitializer CreateInitializer(string dbPath, bool failAfterWriting)
    {
        DatabaseOptions options = new()
        {
            DbPath      = dbPath,
            BackupsPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups"),
        };

        return new WritingSeedInitializer(new SqliteConnectionFactory(dbPath), options, failAfterWriting);
    }

    private async Task<bool> MarkerTableExistsAsync(string dbPath)
    {
        using SqliteConnection connection = new($"Data Source={dbPath}");
        await connection.OpenAsync(TestContext.CancellationToken);

        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;", new { name = MarkerTable }) == 1;
    }
}
