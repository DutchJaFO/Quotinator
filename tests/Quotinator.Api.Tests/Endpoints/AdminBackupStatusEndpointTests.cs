using System.Net;
using System.Text.Json;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>
/// The status endpoint (#349) — can a backup be taken right now, and where does storage stand against
/// both of the limits that govern it.
/// </summary>
[TestClass]
public class AdminBackupStatusEndpointTests
{
    private const string Status = "/api/v1/admin/backups/status";
    private const string Create = "/api/v1/admin/backups/create";

    /// <summary>When a backup is possible, the endpoint says so — the positive control for this whole suite.</summary>
    [TestMethod]
    public async Task GetStatus_WhenABackupIsPossible_SaysSo()
    {
        using BackupTestHarness harness = new BackupTestHarness();

        JsonDocument doc = await GetStatusAsync(harness);

        Assert.IsTrue(doc.RootElement.GetProperty("canBackUp").GetBoolean());

        // Null properties are omitted rather than serialised as null, app-wide — so "no obstacle" is
        // an absent key or an explicit null, and both mean the same thing here.
        bool obstacleReported = doc.RootElement.TryGetProperty("obstacle", out JsonElement obstacle)
                             && obstacle.ValueKind != JsonValueKind.Null;
        Assert.IsFalse(obstacleReported, "no obstacle is named when a backup is possible");
        Assert.IsEmpty(doc.RootElement.GetProperty("remedies").EnumerateArray().ToArray());
    }

    /// <summary>When it is not possible, the obstacle is named and its remedies come with it.</summary>
    [TestMethod]
    public async Task GetStatus_WhenABackupIsNotPossible_NamesTheObstacle()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.Db.Readiness = BackupOutcome.DestinationDirectoryNotWritable;

        JsonDocument doc = await GetStatusAsync(harness);

        Assert.IsFalse(doc.RootElement.GetProperty("canBackUp").GetBoolean());
        Assert.AreEqual(nameof(BackupOutcome.DestinationDirectoryNotWritable),
                        doc.RootElement.GetProperty("obstacle").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("cause").GetString()));
        Assert.IsGreaterThan(0, doc.RootElement.GetProperty("remedies").GetArrayLength());
    }

    /// <summary>Used, the operating quota, the absolute ceiling and the percentage are all reported.</summary>
    [TestMethod]
    public async Task GetStatus_ReportsUsedQuotaCeilingAndPercentage()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("one.db", sizeBytes: 1000);
        harness.WriteBackup("two.db", sizeBytes: 2000);

        JsonElement storage = (await GetStatusAsync(harness)).RootElement.GetProperty("storage");

        Assert.AreEqual(3000, storage.GetProperty("usedBytes").GetInt64());
        Assert.AreEqual(2,    storage.GetProperty("fileCount").GetInt32());
        Assert.AreEqual(BackupStorageBudget.BytesPerGigabyte, storage.GetProperty("ceilingBytes").GetInt64());
        Assert.AreEqual(DatabaseOptions.DefaultBackupQuotaPercent, storage.GetProperty("quotaPercent").GetInt32());
        Assert.AreEqual(BackupStorageBudget.BytesPerGigabyte * 90 / 100, storage.GetProperty("quotaBytes").GetInt64());
        Assert.IsGreaterThan(0d, storage.GetProperty("usedPercentOfCeiling").GetDouble());
        Assert.AreEqual(storage.GetProperty("quotaBytes").GetInt64() - 3000,
                        storage.GetProperty("remainingAgainstQuotaBytes").GetInt64());
    }

    /// <summary>
    /// Whether the reserve between quota and ceiling is being relied on is reported, not inferred.
    /// <para>
    /// The quota is squeezed to 1% rather than the files being made enormous: the ceiling is
    /// configured in whole gigabytes, so crossing a 90% quota honestly would mean writing most of a
    /// gigabyte to a temp folder on every run.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task GetStatus_ReportsWhetherTheReserveAboveTheQuotaIsInUse()
    {
        using BackupTestHarness belowQuota = new BackupTestHarness(backupQuotaPercent: 1);
        belowQuota.WriteBackup("small.db", sizeBytes: 1024);
        Assert.IsFalse((await GetStatusAsync(belowQuota)).RootElement.GetProperty("storage").GetProperty("reserveInUse").GetBoolean());

        using BackupTestHarness aboveQuota = new BackupTestHarness(backupQuotaPercent: 1);
        aboveQuota.WriteBackup("large.db", sizeBytes: 11 * 1024 * 1024);
        JsonElement storage = (await GetStatusAsync(aboveQuota)).RootElement.GetProperty("storage");

        Assert.IsTrue(storage.GetProperty("reserveInUse").GetBoolean());
        Assert.AreEqual(0, storage.GetProperty("remainingAgainstQuotaBytes").GetInt64());
        Assert.IsGreaterThan(0L, storage.GetProperty("remainingAgainstCeilingBytes").GetInt64(),
                             "the reserve is in use, not exhausted — the ceiling still has room");
    }

    /// <summary>
    /// Real free disk space is reported alongside the quota rather than folded into it. The two are
    /// independent constraints and the backup path checks both.
    /// </summary>
    [TestMethod]
    public async Task GetStatus_ReportsRealFreeDiskSpaceSeparatelyFromTheQuota()
    {
        const long FreeBytes = 123_456_789L;
        using BackupTestHarness harness = new BackupTestHarness(diskSpace: new FixedDiskSpaceProvider(FreeBytes));
        harness.WriteBackup("one.db", sizeBytes: 1000);

        JsonElement storage = (await GetStatusAsync(harness)).RootElement.GetProperty("storage");

        Assert.AreEqual(FreeBytes, storage.GetProperty("freeDiskBytes").GetInt64());
        Assert.AreNotEqual(FreeBytes, storage.GetProperty("quotaBytes").GetInt64());
        Assert.AreNotEqual(FreeBytes, storage.GetProperty("remainingAgainstCeilingBytes").GetInt64());
    }

    /// <summary>No backups is zero used, not an error.</summary>
    [TestMethod]
    public async Task GetStatus_NoBackupsExist_ReportsZeroUsedNotAnError()
    {
        using BackupTestHarness harness = new BackupTestHarness();

        JsonElement storage = (await GetStatusAsync(harness)).RootElement.GetProperty("storage");

        Assert.AreEqual(0, storage.GetProperty("usedBytes").GetInt64());
        Assert.AreEqual(0, storage.GetProperty("fileCount").GetInt32());
    }

    /// <summary>
    /// The endpoint answers while the database is degraded, which is the state it exists for — and
    /// still reports the real figures rather than a placeholder.
    /// </summary>
    [TestMethod]
    public async Task GetStatus_ReadsNoDatabaseContent_AndAnswersWhileDegraded()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("one.db", sizeBytes: 4096);
        harness.MarkDatabaseUnhealthy();

        JsonDocument doc = await GetStatusAsync(harness);

        Assert.AreEqual(4096, doc.RootElement.GetProperty("storage").GetProperty("usedBytes").GetInt64());
    }

    /// <summary>
    /// What status promises is what create actually does, in both directions. A status endpoint that
    /// says yes where create then refuses is worse than no status endpoint at all.
    /// </summary>
    [TestMethod]
    public async Task GetStatus_AgreesWithWhatACreateAttemptActuallyDoes()
    {
        using BackupTestHarness possible = new BackupTestHarness();
        Assert.IsTrue((await GetStatusAsync(possible)).RootElement.GetProperty("canBackUp").GetBoolean());
        HttpResponseMessage created = await possible.AuthenticatedClient().PostAsync(Create, null, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);

        using BackupTestHarness refused = new BackupTestHarness();
        refused.Db.Readiness  = BackupOutcome.BudgetExceeded;
        refused.Db.RefuseWith = BackupOutcome.BudgetExceeded;
        Assert.IsFalse((await GetStatusAsync(refused)).RootElement.GetProperty("canBackUp").GetBoolean());
        HttpResponseMessage conflict = await refused.AuthenticatedClient().PostAsync(Create, null, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    /// <summary>Status sits behind the admin API key like every other route in the group.</summary>
    [TestMethod]
    public async Task GetStatus_WithoutApiKey_Returns401()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        HttpResponseMessage response = await harness.AnonymousClient().GetAsync(Status, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<JsonDocument> GetStatusAsync(BackupTestHarness harness)
    {
        HttpResponseMessage response = await harness.AuthenticatedClient().GetAsync(Status, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    /// <summary>Reports a known number of free bytes, so the figure can be told apart from every quota figure beside it.</summary>
    /// <param name="freeBytes">What to report as available.</param>
    private sealed class FixedDiskSpaceProvider(long freeBytes) : IDiskSpaceProvider
    {
        public long GetAvailableFreeSpaceBytes(string path) => freeBytes;
    }

    public TestContext TestContext { get; set; }
}
