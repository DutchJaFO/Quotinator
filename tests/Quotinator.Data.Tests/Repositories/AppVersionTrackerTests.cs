using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Repositories;

/// <summary>
/// Exercises <see cref="AppVersionTracker"/> — introduced by #81 as a single upserted row, reshaped by
/// #312 into an append-only application/version history.
/// </summary>
[TestClass]
public class AppVersionTrackerTests
{
    // The table's real migration sequence: #81's CREATE, then #312's Application column and its
    // SequenceNumber counter. Replaying it rather than hand-writing the current shape keeps the fixture
    // honest — and is what lets the legacy-row test below seed a genuinely pre-#312 row.
    private static readonly string[] Schema =
    [
        AppVersionMigrations.CreateAppVersionTable,
        AppVersionHistoryMigrations.AddApplicationColumn,
        AppVersionHistoryMigrations.AddSequenceNumberColumn,
    ];

    /// <summary>Reading before the table exists (before migrations run, per the whole point of this tracker) returns null, not an exception.</summary>
    [TestMethod]
    public async Task GetLastActiveAsync_TableMissing_ReturnsNull()
    {
        using TempDatabase temp = new([]);
        AppVersionTracker tracker = new(temp.ConnectionFactory);

        Assert.IsNull(await tracker.GetLastActiveAsync());
    }

    /// <summary>A table that exists but has never been written to also reads as null.</summary>
    [TestMethod]
    public async Task GetLastActiveAsync_TableEmpty_ReturnsNull()
    {
        using TempDatabase temp = new(Schema);
        AppVersionTracker tracker = new(temp.ConnectionFactory);

        Assert.IsNull(await tracker.GetLastActiveAsync());
    }

    /// <summary>Application and Version round-trip as separate values — never one concatenated string.</summary>
    [TestMethod]
    public async Task RecordCurrentAsync_FirstCall_StoresApplicationAndVersionSeparately()
    {
        using TempDatabase temp = new(Schema);
        AppVersionTracker tracker = new(temp.ConnectionFactory);

        await tracker.RecordCurrentAsync("Quotinator.Api", "1.8.3");

        AppVersionRecord? recorded = await tracker.GetLastActiveAsync();
        Assert.IsNotNull(recorded);
        Assert.AreEqual("Quotinator.Api", recorded.Application);
        Assert.AreEqual("1.8.3", recorded.Version);

        // Assert against the stored columns too: a record reassembled correctly in C# proves nothing
        // about whether the two values were actually persisted apart.
        using SqliteConnection connection = (SqliteConnection)temp.ConnectionFactory.CreateConnection();
        await connection.OpenAsync(TestContext.CancellationToken);
        string? application = await connection.ExecuteScalarAsync<string>("SELECT Application FROM System_AppVersion;");
        string? version     = await connection.ExecuteScalarAsync<string>("SELECT Version FROM System_AppVersion;");
        Assert.AreEqual("Quotinator.Api", application);
        Assert.AreEqual("1.8.3", version);
    }

    /// <summary>Recording the same application+version twice appends nothing — a restart on the same build must not grow the table.</summary>
    [TestMethod]
    public async Task RecordCurrentAsync_SamePairTwice_AppendsOnlyOnce()
    {
        using TempDatabase temp = new(Schema);
        AppVersionTracker tracker = new(temp.ConnectionFactory);

        AppVersionRecord first  = await tracker.RecordCurrentAsync("Quotinator.Api", "1.8.3");
        AppVersionRecord second = await tracker.RecordCurrentAsync("Quotinator.Api", "1.8.3");

        Assert.AreEqual(first.Id, second.Id, "The same pair must resolve to the same row, not a new one.");
        Assert.AreEqual(1, await RowCountAsync(temp));
    }

    /// <summary>A new version appends a row rather than overwriting — this is what makes provenance references stay correct across an upgrade.</summary>
    [TestMethod]
    public async Task RecordCurrentAsync_NewVersion_AppendsWithoutOverwritingHistory()
    {
        using TempDatabase temp = new(Schema);
        AppVersionTracker tracker = new(temp.ConnectionFactory);

        AppVersionRecord older = await tracker.RecordCurrentAsync("Quotinator.Api", "1.8.3");
        AppVersionRecord newer = await tracker.RecordCurrentAsync("Quotinator.Api", "1.9.0");

        Assert.AreNotEqual(older.Id, newer.Id);
        Assert.AreEqual(2, await RowCountAsync(temp));

        // The older row must still be readable and unchanged — a notification written under 1.8.3
        // references it by id, and that reference must not start meaning 1.9.0.
        using SqliteConnection connection = (SqliteConnection)temp.ConnectionFactory.CreateConnection();
        await connection.OpenAsync(TestContext.CancellationToken);
        string? storedVersion = await connection.ExecuteScalarAsync<string>(
            "SELECT Version FROM System_AppVersion WHERE LOWER(Id) = LOWER(@id);",
            new { id = older.Id.ToString() });
        Assert.AreEqual("1.8.3", storedVersion, "An earlier version's row must stay frozen after a newer version is recorded.");
    }

    /// <summary>The same version under a different application is a genuinely different entry — the pair is the identity, not the version alone.</summary>
    [TestMethod]
    public async Task RecordCurrentAsync_SameVersionDifferentApplication_AppendsSeparately()
    {
        using TempDatabase temp = new(Schema);
        AppVersionTracker tracker = new(temp.ConnectionFactory);

        AppVersionRecord api  = await tracker.RecordCurrentAsync("Quotinator.Api", "1.8.3");
        AppVersionRecord tool = await tracker.RecordCurrentAsync("Quotinator.Tools.DbInspector", "1.8.3");

        Assert.AreNotEqual(api.Id, tool.Id);
        Assert.AreEqual(2, await RowCountAsync(temp));
    }

    /// <summary>
    /// "Last active" is the most recent row, not an arbitrary one — the whole basis of #81's catch-up
    /// range. Writing all three back-to-back is the point, not incidental: <c>DateCreated</c> is stored
    /// at second resolution, so these rows normally share one identical timestamp and nothing in it can
    /// separate them. An earlier <c>Id DESC</c> tie-break made this fail intermittently — a random GUID
    /// decided which version "ran last" — which is why the ordering rests on an explicit counter now.
    /// </summary>
    [TestMethod]
    public async Task GetLastActiveAsync_SeveralVersionsWithinOneTimestamp_ReturnsTheOneWrittenLast()
    {
        using TempDatabase temp = new(Schema);
        AppVersionTracker tracker = new(temp.ConnectionFactory);

        await tracker.RecordCurrentAsync("Quotinator.Api", "1.8.1");
        await tracker.RecordCurrentAsync("Quotinator.Api", "1.8.2");
        AppVersionRecord latest = await tracker.RecordCurrentAsync("Quotinator.Api", "1.8.3");

        AppVersionRecord? lastActive = await tracker.GetLastActiveAsync();
        Assert.IsNotNull(lastActive);
        Assert.AreEqual(latest.Id, lastActive.Id);
        Assert.AreEqual("1.8.3", lastActive.Version);
    }

    /// <summary>Each recorded row takes the next sequence number, so the history has an explicit order rather than an inferred one.</summary>
    [TestMethod]
    public async Task RecordCurrentAsync_EachCall_TakesTheNextSequenceNumber()
    {
        using TempDatabase temp = new(Schema);
        AppVersionTracker tracker = new(temp.ConnectionFactory);

        await tracker.RecordCurrentAsync("Quotinator.Api", "1.8.1");
        await tracker.RecordCurrentAsync("Quotinator.Api", "1.8.2");

        using SqliteConnection connection = (SqliteConnection)temp.ConnectionFactory.CreateConnection();
        await connection.OpenAsync(TestContext.CancellationToken);
        List<long> sequences = [.. await connection.QueryAsync<long>(
            "SELECT SequenceNumber FROM System_AppVersion ORDER BY SequenceNumber;")];

        Assert.AreSequenceEqual<long>([1, 2], sequences);
    }

    /// <summary>
    /// <c>SequenceNumber</c> is the only thing that decides which entry is most recent — a row carrying
    /// a newer <c>DateCreated</c> but an older sequence does not win. This is the inverse of the defect
    /// that prompted the column: the timestamp is not consulted, so its second resolution cannot make
    /// the answer arbitrary again.
    /// </summary>
    [TestMethod]
    public async Task GetLastActiveAsync_RowWithNewerTimestampButOlderSequence_DoesNotWin()
    {
        using TempDatabase temp = new(Schema);
        AppVersionTracker tracker = new(temp.ConnectionFactory);

        AppVersionRecord current = await tracker.RecordCurrentAsync("Quotinator.Api", "1.8.3");

        using (SqliteConnection seed = (SqliteConnection)temp.ConnectionFactory.CreateConnection())
        {
            await seed.OpenAsync(TestContext.CancellationToken);
            await seed.ExecuteAsync(
                "INSERT INTO System_AppVersion (Id, Application, Version, DateCreated, SequenceNumber) " +
                "VALUES (@id, 'Quotinator.Api', '1.7.0', '2999-01-01 00:00:00', 0);",
                new { id = Guid.NewGuid().ToString() });
        }

        AppVersionRecord? lastActive = await tracker.GetLastActiveAsync();
        Assert.IsNotNull(lastActive);
        Assert.AreEqual(current.Id, lastActive.Id,
            "A far-future timestamp must not outrank the sequence — DateCreated is not part of this ordering at all.");
    }

    /// <summary>
    /// A row written by #81's version-only tracker carries no application name. It must stay readable
    /// (that version really did run last, which is what #81's catch-up range needs), keep its null
    /// <see cref="AppVersionRecord.Application"/> rather than having one invented, and — because the
    /// pair is the identity — not suppress the properly attributed row the upgraded app records.
    /// </summary>
    [TestMethod]
    public async Task RecordCurrentAsync_AfterLegacyRowWithNoApplication_ReadsItThenAppendsAttributedRow()
    {
        // Seeded between the two #312 migrations rather than after both, so this is a genuinely
        // pre-SequenceNumber row and the migration's own backfill is what gives it a position.
        using TempDatabase temp = new([AppVersionMigrations.CreateAppVersionTable, AppVersionHistoryMigrations.AddApplicationColumn]);

        using (SqliteConnection seed = (SqliteConnection)temp.ConnectionFactory.CreateConnection())
        {
            await seed.OpenAsync(TestContext.CancellationToken);
            await seed.ExecuteAsync(
                "INSERT INTO System_AppVersion (Id, Version, DateCreated) VALUES (@id, '1.8.2', @now);",
                new { id = Guid.NewGuid().ToString(), now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
            await seed.ExecuteAsync(AppVersionHistoryMigrations.AddSequenceNumberColumn);
        }

        AppVersionTracker tracker = new(temp.ConnectionFactory);
        AppVersionRecord? legacy = await tracker.GetLastActiveAsync();

        Assert.IsNotNull(legacy);
        Assert.IsNull(legacy.Application, "A row predating #312 genuinely has no application name — it must not be invented.");
        Assert.AreEqual("1.8.2", legacy.Version);

        AppVersionRecord attributed = await tracker.RecordCurrentAsync("Quotinator.Api", "1.8.2");

        Assert.AreNotEqual(legacy.Id, attributed.Id, "An unattributed legacy row is not the same entry as an attributed one.");
        Assert.AreEqual("Quotinator.Api", attributed.Application);
        Assert.AreEqual(2, await RowCountAsync(temp));
    }

    private static async Task<int> RowCountAsync(TempDatabase temp)
    {
        using SqliteConnection connection = (SqliteConnection)temp.ConnectionFactory.CreateConnection();
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_AppVersion;");
    }

    public TestContext TestContext { get; set; }
}
