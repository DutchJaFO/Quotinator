using System.Net;
using System.Text.Json;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>
/// Taking a backup on demand (#349) — the action that makes a restore point something an operator can
/// ask for, rather than only a side effect of a migration, a seed or a Reset.
/// </summary>
[TestClass]
public class AdminBackupCreateEndpointTests
{
    private const string Backups = "/api/v1/admin/backups";
    private const string Create  = "/api/v1/admin/backups/create";

    /// <summary>A create writes a backup and tells the caller what it produced.</summary>
    [TestMethod]
    public async Task Create_WritesABackupFile_AndNamesItInTheResponse()
    {
        using BackupTestHarness harness = new BackupTestHarness();

        HttpResponseMessage response = await harness.AuthenticatedClient().PostAsync(Create, null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        string? name = doc.RootElement.GetProperty("name").GetString();

        Assert.IsFalse(string.IsNullOrWhiteSpace(name));
        Assert.Contains(name!, harness.FilesOnDisk());
    }

    /// <summary>
    /// The endpoints compose: what create writes, list shows and download returns. The operator's
    /// actual loop, asserted end to end rather than one endpoint at a time.
    /// </summary>
    [TestMethod]
    public async Task Create_TheCreatedFileAppearsInTheList_AndCanBeDownloaded()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        HttpClient client = harness.AuthenticatedClient();

        HttpResponseMessage created = await client.PostAsync(Create, null, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        JsonDocument createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync(TestContext.CancellationToken));
        string name = createdDoc.RootElement.GetProperty("name").GetString()!;

        JsonDocument listed = JsonDocument.Parse(await client.GetStringAsync(Backups, TestContext.CancellationToken));
        string[] listedNames = [.. listed.RootElement.GetProperty("items").EnumerateArray()
                                                    .Select(i => i.GetProperty("name").GetString()!)];
        Assert.Contains(name, listedNames);

        HttpResponseMessage downloaded = await client.GetAsync($"{Backups}/{name}/content", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, downloaded.StatusCode);
        byte[] served   = await downloaded.Content.ReadAsByteArrayAsync(TestContext.CancellationToken);
        byte[] onDisk   = await File.ReadAllBytesAsync(Path.Combine(harness.BackupsPath, name), TestContext.CancellationToken);
        Assert.AreSequenceEqual(onDisk, served);
    }

    /// <summary>
    /// A create that cannot take a backup refuses with the obstacle and its remedies — never a success
    /// that produced no file. The same shape a refused Reset returns.
    /// </summary>
    [TestMethod]
    public async Task Create_WhenNoBackupCanBeTaken_RefusesWithTheObstacleAndItsRemedies()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.Db.RefuseWith = BackupOutcome.BudgetExceeded;

        HttpResponseMessage response = await harness.AuthenticatedClient().PostAsync(Create, null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual(nameof(BackupOutcome.BudgetExceeded), doc.RootElement.GetProperty("backupObstacle").GetString());
        Assert.IsGreaterThan(0, doc.RootElement.GetProperty("remedies").GetArrayLength());
        Assert.IsEmpty(harness.FilesOnDisk());
    }

    /// <summary>
    /// A backup can be downloaded immediately after being created.
    /// <para>
    /// The exact T1 failure (#349, 2026-08-29): create returned `201`, and downloading that same file
    /// answered an unhandled `500` because the pooled SQLite connection that wrote it still held the
    /// handle. `Create_TheCreatedFileAppearsInTheList_AndCanBeDownloaded` did not catch it because the
    /// harness's stub writes the file with plain IO rather than through a SQLite connection — this one
    /// is guarded at the Data layer instead, by
    /// `DatabaseBackupQuotaTests.CreateBackupAsync_LeavesNoHandleOnTheFileItWrote`, and kept here as
    /// the endpoint-level statement of the same guarantee.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task Create_ThenDownloadImmediately_Succeeds()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        HttpClient client = harness.AuthenticatedClient();

        HttpResponseMessage created = await client.PostAsync(Create, null, TestContext.CancellationToken);
        string name = JsonDocument.Parse(await created.Content.ReadAsStringAsync(TestContext.CancellationToken))
                                  .RootElement.GetProperty("name").GetString()!;

        HttpResponseMessage downloaded = await client.GetAsync($"{Backups}/{name}/content", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.IsGreaterThan(0, (await downloaded.Content.ReadAsByteArrayAsync(TestContext.CancellationToken)).Length);
    }

    /// <summary>A creation is recorded in the audit trail, under the operation declared for exactly this.</summary>
    [TestMethod]
    public async Task Create_WritesAnAuditEntry()
    {
        using BackupTestHarness harness = new BackupTestHarness();

        await harness.AuthenticatedClient().PostAsync(Create, null, TestContext.CancellationToken);

        Assert.HasCount(1, harness.Audit.Entries);
        Assert.AreEqual(AuditOperation.Backup, harness.Audit.Entries[0].Operation);
    }

    /// <summary>A refused create records nothing — there is no backup to account for.</summary>
    [TestMethod]
    public async Task Create_WhenRefused_WritesNoAuditEntry()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.Db.RefuseWith = BackupOutcome.DestinationDirectoryNotWritable;

        await harness.AuthenticatedClient().PostAsync(Create, null, TestContext.CancellationToken);

        Assert.IsEmpty(harness.Audit.Entries);
    }

    /// <summary>Creating sits behind the admin API key, and an unauthorised call takes no backup.</summary>
    [TestMethod]
    public async Task Create_WithoutApiKey_Returns401()
    {
        using BackupTestHarness harness = new BackupTestHarness();

        HttpResponseMessage response = await harness.AnonymousClient().PostAsync(Create, null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, harness.Db.CreateCalls, "a rejected call must not reach the backup path at all");
        Assert.IsEmpty(harness.FilesOnDisk());
    }

    public TestContext TestContext { get; set; }
}
