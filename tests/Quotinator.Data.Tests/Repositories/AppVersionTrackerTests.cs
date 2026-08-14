using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Repositories;

/// <summary>Exercises <see cref="AppVersionTracker"/> (#81).</summary>
[TestClass]
public class AppVersionTrackerTests
{
    /// <summary>Reading before the table exists (before migrations run, per the whole point of this tracker) returns null, not an exception.</summary>
    [TestMethod]
    public async Task GetLastActiveVersionAsync_TableMissing_ReturnsNull()
    {
        using TempDatabase temp = new([]);
        AppVersionTracker tracker = new(temp.ConnectionFactory);

        string? version = await tracker.GetLastActiveVersionAsync();

        Assert.IsNull(version);
    }

    /// <summary>A table that exists but has never been written to also reads as null.</summary>
    [TestMethod]
    public async Task GetLastActiveVersionAsync_TableEmpty_ReturnsNull()
    {
        using TempDatabase temp = new([AppVersionMigrations.CreateAppVersionTable]);
        AppVersionTracker tracker = new(temp.ConnectionFactory);

        string? version = await tracker.GetLastActiveVersionAsync();

        Assert.IsNull(version);
    }

    /// <summary>The first recorded version round-trips back out.</summary>
    [TestMethod]
    public async Task RecordCurrentVersionAsync_FirstCall_InsertsRow()
    {
        using TempDatabase temp = new([AppVersionMigrations.CreateAppVersionTable]);
        AppVersionTracker tracker = new(temp.ConnectionFactory);

        await tracker.RecordCurrentVersionAsync("1.8.3");
        string? version = await tracker.GetLastActiveVersionAsync();

        Assert.AreEqual("1.8.3", version);
    }

    /// <summary>A second call updates the same row in place rather than inserting a duplicate — exactly one non-deleted row is the whole point of this table.</summary>
    [TestMethod]
    public async Task RecordCurrentVersionAsync_CalledTwice_UpdatesInPlaceNotDuplicate()
    {
        using TempDatabase temp = new([AppVersionMigrations.CreateAppVersionTable]);
        AppVersionTracker tracker = new(temp.ConnectionFactory);

        await tracker.RecordCurrentVersionAsync("1.8.2");
        await tracker.RecordCurrentVersionAsync("1.8.3");

        using var connection = (Microsoft.Data.Sqlite.SqliteConnection)temp.ConnectionFactory.CreateConnection();
        await connection.OpenAsync(TestContext.CancellationToken);
        int rowCount = await Dapper.SqlMapper.ExecuteScalarAsync<int>(connection, "SELECT COUNT(*) FROM System_AppVersion;");

        Assert.AreEqual(1, rowCount, "Recording the version twice must update the one existing row, not insert a second.");
        Assert.AreEqual("1.8.3", await tracker.GetLastActiveVersionAsync());
    }

    public TestContext TestContext { get; set; }
}
