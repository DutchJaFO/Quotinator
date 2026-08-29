using System.Net;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>
/// Downloading a stored backup (#349) — the half that lets a restore point survive the container it
/// was taken in.
/// </summary>
[TestClass]
public class AdminBackupDownloadEndpointTests
{
    private const string Backups = "/api/v1/admin/backups";

    /// <summary>
    /// The download is the stored file, unaltered. Byte-for-byte rather than by length: a backup that
    /// does not round-trip exactly is not a restore point, and a length check would pass against a
    /// build that returned the right number of wrong bytes.
    /// </summary>
    [TestMethod]
    public async Task Download_ReturnsTheFilesBytes_ByteForByte()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        byte[] written = harness.WriteBackup("quotinatordata_v5_20260101T101010101Z.db", sizeBytes: 512);

        HttpResponseMessage response = await harness.AuthenticatedClient()
            .GetAsync($"{Backups}/quotinatordata_v5_20260101T101010101Z.db/content", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        byte[] served = await response.Content.ReadAsByteArrayAsync(TestContext.CancellationToken);
        Assert.AreSequenceEqual(written, served);
    }

    /// <summary>The response names the file, so what the operator saves is identifiable later.</summary>
    [TestMethod]
    public async Task Download_SetsAnAttachmentNameMatchingTheStoredFile()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("quotinatordata_v5_20260101T101010101Z.db");

        HttpResponseMessage response = await harness.AuthenticatedClient()
            .GetAsync($"{Backups}/quotinatordata_v5_20260101T101010101Z.db/content", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(response.Content.Headers.ContentDisposition);
        Assert.AreEqual("attachment", response.Content.Headers.ContentDisposition!.DispositionType);

        string? served = response.Content.Headers.ContentDisposition.FileNameStar
                      ?? response.Content.Headers.ContentDisposition.FileName?.Trim('"');
        Assert.AreEqual("quotinatordata_v5_20260101T101010101Z.db", served);
    }

    /// <summary>Downloading a name that does not exist is a 404, not an empty file.</summary>
    [TestMethod]
    public async Task Download_UnknownName_Returns404()
    {
        using BackupTestHarness harness = new BackupTestHarness();

        HttpResponseMessage response = await harness.AuthenticatedClient()
            .GetAsync($"{Backups}/never-existed.db/content", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// A traversal attempt is rejected <em>and</em> serves nothing — the read-side counterpart of the
    /// delete guard, asserted on both halves for the same reason.
    /// </summary>
    [TestMethod]
    public async Task Download_PathTraversalAttempt_IsRejectedAndServesNothing()
    {
        using BackupTestHarness harness = new BackupTestHarness();

        string outsidePath = Path.Combine(Directory.GetParent(harness.BackupsPath)!.FullName, $"outside-{Guid.NewGuid():N}.db");
        byte[] secret = [9, 8, 7, 6, 5];
        File.WriteAllBytes(outsidePath, secret);

        try
        {
            string traversal = Uri.EscapeDataString($"../{Path.GetFileName(outsidePath)}");
            HttpResponseMessage response = await harness.AuthenticatedClient()
                .GetAsync($"{Backups}/{traversal}/content", TestContext.CancellationToken);

            Assert.AreNotEqual(HttpStatusCode.OK, response.StatusCode);
            byte[] served = await response.Content.ReadAsByteArrayAsync(TestContext.CancellationToken);
            Assert.AreNotSequenceEqual(secret, served, "the file outside the backups folder must not be served");
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    /// <summary>
    /// A traversal that actually reaches the handler is refused there, and serves nothing.
    /// <para>
    /// The encoded <c>../</c> case above is rejected by ASP.NET's routing before any handler runs, so
    /// it stays green even with the guard removed — measured by mutation. A backslash is not a URL
    /// path separator, so it arrives intact and the application has to refuse it itself.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task Download_BackslashTraversalReachingTheHandler_IsRejectedAndServesNothing()
    {
        using BackupTestHarness harness = new BackupTestHarness();

        string outsidePath = Path.Combine(Directory.GetParent(harness.BackupsPath)!.FullName, $"outside-{Guid.NewGuid():N}.db");
        byte[] secret = [9, 8, 7, 6, 5];
        File.WriteAllBytes(outsidePath, secret);

        try
        {
            string traversal = Uri.EscapeDataString($"..\\{Path.GetFileName(outsidePath)}");
            HttpResponseMessage response = await harness.AuthenticatedClient()
                .GetAsync($"{Backups}/{traversal}/content", TestContext.CancellationToken);

            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            byte[] served = await response.Content.ReadAsByteArrayAsync(TestContext.CancellationToken);
            Assert.AreNotSequenceEqual(secret, served, "the file outside the backups folder must not be served");
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    /// <summary>
    /// A backup that cannot be opened answers 409, never an unhandled 500 — the read-side counterpart
    /// of the delete endpoint's own refusal, and the shape T1 found missing here.
    /// </summary>
    [TestMethod]
    public async Task Download_FileCannotBeOpened_Returns409NotAnUnhandled500()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("locked.db");

        using FileStream exclusive = new FileStream(
            Path.Combine(harness.BackupsPath, "locked.db"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        HttpResponseMessage response = await harness.AuthenticatedClient()
            .GetAsync($"{Backups}/locked.db/content", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Downloading sits behind the admin API key.</summary>
    [TestMethod]
    public async Task Download_WithoutApiKey_Returns401()
    {
        using BackupTestHarness harness = new BackupTestHarness();
        harness.WriteBackup("present.db");

        HttpResponseMessage response = await harness.AnonymousClient()
            .GetAsync($"{Backups}/present.db/content", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public TestContext TestContext { get; set; }
}
