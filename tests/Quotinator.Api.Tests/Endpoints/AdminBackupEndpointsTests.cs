using System.Net;
using System.Text.Json;
using Quotinator.Data.Entities;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>
/// The list and delete halves of the backup endpoints (#349), plus the guard both share and the
/// reachability every route in the group depends on.
/// </summary>
[TestClass]
public class AdminBackupEndpointsTests
{
    private const string List = "/api/v1/admin/backups";

    /// <summary>The one file a delete must leave behind, held as a field per CA1861.</summary>
    private static readonly string[] SurvivingFile = ["keep-me.db"];

    /// <summary>The list reports every backup with the facts needed to choose one — name, size, when taken.</summary>
    [TestMethod]
    public async Task GetBackups_ReturnsEachBackupWithItsNameSizeAndTimestamp()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("quotinatordata_v5_20260101T101010101Z.db", sizeBytes: 128);
        harness.WriteBackup("quotinatordata_v5_20260102T101010101Z.db", sizeBytes: 256);

        HttpResponseMessage response = await harness.AuthenticatedClient().GetAsync(List, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        JsonElement items = doc.RootElement.GetProperty("items");
        Assert.AreEqual(2, items.GetArrayLength());

        JsonElement first = items[0];
        Assert.IsTrue(first.TryGetProperty("name", out JsonElement name));
        Assert.IsTrue(first.TryGetProperty("sizeBytes", out JsonElement size));
        Assert.IsTrue(first.TryGetProperty("takenAtUtc", out JsonElement takenAt));

        // Newest first, so the size that goes with the newest name is the one asserted — proving the
        // three facts belong to the same file rather than each merely being present somewhere.
        Assert.AreEqual("quotinatordata_v5_20260102T101010101Z.db", name.GetString());
        Assert.AreEqual(256, size.GetInt64());
        Assert.AreNotEqual(default, takenAt.GetDateTime());
    }

    /// <summary>An empty backups folder is an empty page, not a 404 — nothing has been backed up yet.</summary>
    [TestMethod]
    public async Task GetBackups_NoBackupsExist_ReturnsAnEmptyPageNotA404()
    {
        using BackupTestHarness harness = new BackupTestHarness();

        HttpResponseMessage response = await harness.AuthenticatedClient().GetAsync(List, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual(0, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(0, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    /// <summary>The list sits behind the admin API key.</summary>
    [TestMethod]
    public async Task GetBackups_WithoutApiKey_Returns401()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        HttpResponseMessage response = await harness.AnonymousClient().GetAsync(List, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A deletion removes the named file and leaves its neighbours alone.</summary>
    [TestMethod]
    public async Task DeleteBackup_RemovesOnlyTheNamedFile()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("keep-me.db");
        harness.WriteBackup("delete-me.db");

        HttpResponseMessage response = await harness.AuthenticatedClient()
            .DeleteAsync($"{List}/delete-me.db", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreSequenceEqual(SurvivingFile, harness.FilesOnDisk());
    }

    /// <summary>A deletion is recorded in the audit trail, naming the file, so it outlives the log.</summary>
    [TestMethod]
    public async Task DeleteBackup_WritesAnAuditEntry()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("delete-me.db");

        await harness.AuthenticatedClient().DeleteAsync($"{List}/delete-me.db", TestContext.CancellationToken);

        Assert.HasCount(1, harness.Audit.Entries);
        AuditEntryEntity entry = harness.Audit.Entries[0];
        Assert.AreEqual(AuditOperation.BackupDeleted, entry.Operation);
        Assert.IsNotNull(entry.RecordId);
        Assert.Contains("delete-me", entry.RecordId!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Deleting a name that does not exist is a 404, distinguishable from a successful removal.</summary>
    [TestMethod]
    public async Task DeleteBackup_UnknownName_Returns404()
    {
        using BackupTestHarness harness = new BackupTestHarness();

        HttpResponseMessage response = await harness.AuthenticatedClient()
            .DeleteAsync($"{List}/never-existed.db", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.IsEmpty(harness.Audit.Entries);
    }

    /// <summary>Deletion sits behind the admin API key.</summary>
    [TestMethod]
    public async Task DeleteBackup_WithoutApiKey_Returns401()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("delete-me.db");

        HttpResponseMessage response = await harness.AnonymousClient()
            .DeleteAsync($"{List}/delete-me.db", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("delete-me.db", harness.FilesOnDisk());
    }

    /// <summary>
    /// A traversal attempt is rejected <em>and</em> removes nothing. Both halves are asserted
    /// deliberately: a status-only assertion would pass against a build that deleted the file first
    /// and reported a rejection afterwards.
    /// </summary>
    [TestMethod]
    public async Task DeleteBackup_PathTraversalAttempt_IsRejectedAndDeletesNothing()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("innocent.db");

        // Written into the parent of the backups folder — the file a successful traversal would reach.
        string outsidePath = Path.Combine(Directory.GetParent(harness.BackupsPath)!.FullName, $"outside-{Guid.NewGuid():N}.db");
        File.WriteAllBytes(outsidePath, [1, 2, 3]);

        try
        {
            string traversal = Uri.EscapeDataString($"../{Path.GetFileName(outsidePath)}");
            HttpResponseMessage response = await harness.AuthenticatedClient()
                .DeleteAsync($"{List}/{traversal}", TestContext.CancellationToken);

            Assert.AreNotEqual(HttpStatusCode.NoContent, response.StatusCode);
            Assert.IsTrue(File.Exists(outsidePath), "the file outside the backups folder must still exist");
            Assert.Contains("innocent.db", harness.FilesOnDisk());
            Assert.IsEmpty(harness.Audit.Entries);
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    /// <summary>
    /// A traversal that actually reaches the handler is refused there.
    /// <para>
    /// This exists because the encoded <c>../</c> case above does not prove the guard: ASP.NET's own
    /// routing rejects that before any handler runs, so it stays green even with the guard removed
    /// entirely — measured by mutation, not assumed. A backslash is not a URL path separator, so it
    /// arrives intact as one route segment and is the application's own problem to refuse. On Windows
    /// it is also a real path separator, which is exactly what makes it dangerous.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task DeleteBackup_BackslashTraversalReachingTheHandler_IsRejectedAndDeletesNothing()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("innocent.db");

        string outsidePath = Path.Combine(Directory.GetParent(harness.BackupsPath)!.FullName, $"outside-{Guid.NewGuid():N}.db");
        File.WriteAllBytes(outsidePath, [1, 2, 3]);

        try
        {
            string traversal = Uri.EscapeDataString($"..\\{Path.GetFileName(outsidePath)}");
            HttpResponseMessage response = await harness.AuthenticatedClient()
                .DeleteAsync($"{List}/{traversal}", TestContext.CancellationToken);

            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.IsTrue(File.Exists(outsidePath), "the file outside the backups folder must still exist");
            Assert.Contains("innocent.db", harness.FilesOnDisk());
            Assert.IsEmpty(harness.Audit.Entries);
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    /// <summary>An absolute path is rejected the same way, and removes nothing.</summary>
    [TestMethod]
    public async Task DeleteBackup_AbsolutePathAttempt_IsRejectedAndDeletesNothing()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("innocent.db");

        string outsidePath = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.db");
        File.WriteAllBytes(outsidePath, [1, 2, 3]);

        try
        {
            HttpResponseMessage response = await harness.AuthenticatedClient()
                .DeleteAsync($"{List}/{Uri.EscapeDataString(outsidePath)}", TestContext.CancellationToken);

            Assert.AreNotEqual(HttpStatusCode.NoContent, response.StatusCode);
            Assert.IsTrue(File.Exists(outsidePath), "the file outside the backups folder must still exist");
            Assert.Contains("innocent.db", harness.FilesOnDisk());
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    /// <summary>
    /// A backup that cannot be removed answers 409, never an unhandled 500.
    /// <para>
    /// Found live during this issue's own T2 pass, against a read-only data directory: the delete
    /// endpoint returned a bare `500`. That is the defect class #348 exists to remove, and the path is
    /// the realistic one — a read-only mount is what degrades startup, and removing old backups is what
    /// the operator is then told to do.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task DeleteBackup_FileCannotBeRemoved_Returns409NotAnUnhandled500()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("locked.db");
        string path = Path.Combine(harness.BackupsPath, "locked.db");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            HttpResponseMessage response = await harness.AuthenticatedClient()
                .DeleteAsync($"{List}/locked.db", TestContext.CancellationToken);

            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
            Assert.IsTrue(File.Exists(path), "a refused removal must leave the backup in place");
            Assert.IsEmpty(harness.Audit.Entries, "nothing was removed, so nothing is recorded as removed");
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    /// <summary>
    /// Every route in the group reaches its handler while the database is degraded — the state they
    /// exist for. Asserted for these routes specifically rather than inferred from #326's
    /// admin-surface property.
    /// </summary>
    [TestMethod]
    public async Task AllRoutes_RemainReachableWhileDegraded()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("present.db");
        harness.MarkDatabaseUnhealthy();
        HttpClient client = harness.AuthenticatedClient();

        // Reaching the handler is the property under test, not what each handler then answers — so a
        // 404 from an unknown name counts, and only a health-gate answer (503) does not.
        (string Method, string Route)[] routes =
        [
            ("GET",    List),
            ("GET",    $"{List}/status"),
            ("GET",    $"{List}/present.db/content"),
            ("POST",   $"{List}/create"),
            ("DELETE", $"{List}/present.db"),
        ];

        foreach ((string method, string route) in routes)
        {
            HttpResponseMessage response = method switch
            {
                "GET"    => await client.GetAsync(route, TestContext.CancellationToken),
                "POST"   => await client.PostAsync(route, null, TestContext.CancellationToken),
                _        => await client.DeleteAsync(route, TestContext.CancellationToken),
            };

            Assert.AreNotEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode,
                $"{method} {route} was answered by the health gate instead of reaching its handler");
            Assert.AreNotEqual(HttpStatusCode.NotFound, response.StatusCode,
                $"{method} {route} is not registered");
        }
    }

    public TestContext TestContext { get; set; }
}
