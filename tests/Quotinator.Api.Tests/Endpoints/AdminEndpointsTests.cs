using System.Data;
using Quotinator.Data.Enums;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class AdminEndpointsTests
{
    private const string TestKey = "test-admin-key";

    private static WebApplicationFactory<Program> CreateFactory(
        string? adminApiKey = null, IDatabaseInitializer? dbInitializer = null, INotificationWriter? notificationWriter = null,
        IAuditEntryWriter? auditWriter = null) =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton(dbInitializer ?? NoOpDatabaseInitializer.Instance);
                services.AddSingleton(auditWriter ?? (IAuditEntryWriter)new NoOpAuditEntryWriter());
                services.AddSingleton<IAuditEntryReader>(new NoOpAuditEntryReader());
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
                services.AddSingleton(notificationWriter ?? (INotificationWriter)NoOpNotificationWriter.Instance);
            });

            // ConfigureAppConfiguration runs after all file-based sources (including
            // appsettings.local.json), so the in-memory value wins for the test.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Quotinator:AdminApiKey"] = adminApiKey
                });
            });
        });

    private static HttpClient CreateClientWithKey(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", TestKey);
        return client;
    }

    // ── GET /admin/database/seed/preview ─────────────────────────────────────

    /// <summary>GET /admin/database/seed/preview is publicly accessible — no API key required.</summary>
    [TestMethod]
    public async Task PreviewSeed_NoKey_Returns200()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/admin/database/seed/preview", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>GET /admin/database/seed/preview returns 200 with the expected shape.</summary>
    [TestMethod]
    public async Task PreviewSeed_Returns200WithPreviewShape()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey);
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/admin/database/seed/preview", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.IsTrue(doc.RootElement.TryGetProperty("files",   out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("reports", out _));
    }

    // ── POST /admin/database/reseed ───────────────────────────────────────────

    /// <summary>POST /admin/database/reseed returns 401 when AdminApiKey is not configured.</summary>
    [TestMethod]
    public async Task ReseedDatabase_NoKeyConfigured_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().PostAsync("/api/v1/admin/database/reseed", null, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>POST /admin/database/reseed returns 401 when the Authorization header is missing.</summary>
    [TestMethod]
    public async Task ReseedDatabase_MissingAuthHeader_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey);
        HttpResponseMessage response = await factory.CreateClient().PostAsync("/api/v1/admin/database/reseed", null, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>POST /admin/database/reseed returns 401 when the wrong key is supplied.</summary>
    [TestMethod]
    public async Task ReseedDatabase_WrongKey_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey);
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", "wrong-key");
        HttpResponseMessage response = await client.PostAsync("/api/v1/admin/database/reseed", null, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>POST /admin/database/reseed returns 200 with the expected stats shape when the correct key is supplied.</summary>
    [TestMethod]
    public async Task ReseedDatabase_CorrectKey_Returns200WithStatsShape()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey);
        HttpResponseMessage response = await CreateClientWithKey(factory).PostAsync("/api/v1/admin/database/reseed", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.IsTrue(doc.RootElement.TryGetProperty("quotes",          out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("sources",         out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("characters",      out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("people",          out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("series",          out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("universes",       out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("stageDirections", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("soundCues",       out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("conversations",   out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("reports",         out _), "#221: per-file report array replacing the old flat duplicates count");
    }

    // ── POST /admin/database/reset ────────────────────────────────────────────

    /// <summary>POST /admin/database/reset returns 401 when AdminApiKey is not configured.</summary>
    [TestMethod]
    public async Task ResetDatabase_NoKeyConfigured_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>POST /admin/database/reset returns 401 when the Authorization header is missing.</summary>
    [TestMethod]
    public async Task ResetDatabase_MissingAuthHeader_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey);
        HttpResponseMessage response = await factory.CreateClient().PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>POST /admin/database/reset returns 401 when the wrong key is supplied.</summary>
    [TestMethod]
    public async Task ResetDatabase_WrongKey_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey);
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", "wrong-key");
        HttpResponseMessage response = await client.PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>POST /admin/database/reset returns 200 with the expected stats shape when the correct key is supplied.</summary>
    [TestMethod]
    public async Task ResetDatabase_CorrectKey_Returns200WithStatsShape()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey);
        HttpResponseMessage response = await CreateClientWithKey(factory).PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.IsTrue(doc.RootElement.TryGetProperty("quotes",          out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("sources",         out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("characters",      out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("people",          out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("series",          out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("universes",       out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("stageDirections", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("soundCues",       out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("conversations",   out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("reports",         out _), "#221: per-file report array replacing the old flat duplicates count");
    }

    // ── #348: a reset that cannot take a backup ───────────────────────────────

    /// <summary>
    /// The regression control for the whole of #348: on a database where a backup succeeds, none of the
    /// refusal machinery is visible. Without this, every other test here could pass while the endpoint
    /// had quietly started refusing everything.
    /// </summary>
    [TestMethod]
    public async Task ResetDatabase_WhenBackupSucceeds_IsUnchanged()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer();
        RecordingAuditWriter audit = new RecordingAuditWriter();
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy, auditWriter: audit);

        HttpResponseMessage response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(spy.ResetRan);
        Assert.DoesNotContain(
            AuditOperation.BackupSkipped, audit.Operations,
            "nothing was skipped, so nothing should claim it was");
    }

    [TestMethod]
    public async Task ResetDatabase_WhenNoBackupCanBeTaken_RefusesWithAStatedFailureRatherThanAnUnhandled500()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer { RefuseWith = BackupOutcome.SourceUnreadable };
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy);

        HttpResponseMessage response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);

        Assert.AreEqual(
            HttpStatusCode.Conflict, response.StatusCode,
            "the pre-#348 behaviour was an unhandled 500 on exactly this state — the one the /health "
            + "reason tells the operator to resolve by resetting");
    }

    [TestMethod]
    public async Task ResetDatabase_WhenNoBackupCanBeTaken_DoesNotRebuildTheDatabase()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer { RefuseWith = BackupOutcome.BudgetExceeded };
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy);

        await CreateClientWithKey(factory).PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);

        Assert.IsFalse(
            spy.ResetRan,
            "refusing has to mean the destructive step did not run — a 409 alongside a completed wipe "
            + "would be the worst of both");
    }

    [TestMethod]
    public async Task ResetDatabase_WhenNoBackupCanBeTaken_ResponseNamesTheCauseAndItsRemedies()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer { RefuseWith = BackupOutcome.BudgetExceeded };
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy);

        HttpResponseMessage response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(
            nameof(BackupOutcome.BudgetExceeded), doc.RootElement.GetProperty("backupObstacle").GetString(),
            "which of the five obstacles it was — the whole point of attributing them");
        Assert.Contains("quota", doc.RootElement.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);

        JsonElement remedies = doc.RootElement.GetProperty("remedies");
        Assert.IsGreaterThan(0, remedies.GetArrayLength(), "an error that names no way out is not actionable");
        string allRemedies = string.Join(" ", remedies.EnumerateArray().Select(r => r.GetString()!));
        Assert.Contains(
            "allowNoBackup", allRemedies, StringComparison.Ordinal,
            "the override is a remedy the caller can act on immediately, so it must be offered");
    }

    /// <summary>
    /// Found live, not by unit test: a corrupt database passes the pre-flight (which inspects storage,
    /// never the database) and then fails inside the table drop, because SQLite will not open the file
    /// at all. The override cannot rescue that — there is nothing to drop — so offering it would name a
    /// remedy that cannot succeed, which is the exact defect #326 fixed for the data-directory case.
    /// </summary>
    [TestMethod]
    public async Task ResetDatabase_WhenTheSourceIsUnreadable_DoesNotOfferAnOverrideThatCannotWork()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer { RefuseWith = BackupOutcome.SourceUnreadable };
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy);

        HttpResponseMessage response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        string allRemedies = string.Join(" ",
            doc.RootElement.GetProperty("remedies").EnumerateArray().Select(r => r.GetString()!));

        Assert.DoesNotContain(
            "allowNoBackup", allRemedies, StringComparison.Ordinal,
            "a reset cannot run against a file SQLite will not open, whatever the caller accepts");
        Assert.Contains("restart", allRemedies, StringComparison.OrdinalIgnoreCase,
            "the remedy that does work is replacing the file from outside the application");
    }

    /// <summary>
    /// Found live on a read-only <c>/data</c>: the override was offered, was used, still refused — and
    /// the response then repeated the same advice that had just failed. Advice already disproved on this
    /// very request is worse than no advice, because it sends the operator round the same loop.
    /// </summary>
    [TestMethod]
    public async Task ResetDatabase_WhenTheOverrideWasTriedAndStillRefused_DoesNotOfferItAgain()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer
        {
            RefuseWith = BackupOutcome.DestinationFileNotWritable,
            RefuseEvenWithOverride = true,
        };
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy);

        HttpResponseMessage response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset?allowNoBackup=true", null, TestContext.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        string allRemedies = string.Join(" ",
            doc.RootElement.GetProperty("remedies").EnumerateArray().Select(r => r.GetString()!));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain(
            "allowNoBackup", allRemedies, StringComparison.Ordinal,
            "it was just tried and did not work — repeating it is advice the request itself disproved");
        Assert.IsGreaterThan(0, allRemedies.Length, "removing the disproved remedy must not leave nothing");
    }

    [TestMethod]
    public async Task ResetDatabase_WithOverride_ProceedsAndRebuilds()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer { RefuseWith = BackupOutcome.BudgetExceeded };
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy);

        HttpResponseMessage response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset?allowNoBackup=true", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(spy.ResetRan);
        Assert.IsTrue(spy.LastAllowNoBackup, "the endpoint must actually forward the override, not just accept it");
    }

    [TestMethod]
    public async Task ResetDatabase_WithOverride_WritesAnAuditEntryRecordingTheSkip()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer { RefuseWith = BackupOutcome.BudgetExceeded };
        RecordingAuditWriter audit = new RecordingAuditWriter();
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy, auditWriter: audit);

        await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset?allowNoBackup=true", null, TestContext.CancellationToken);

        Assert.Contains(
            AuditOperation.BackupSkipped, audit.Operations,
            "a log line rotates away; the audit row is what still answers \"why is there no backup from "
            + "that date\" months later");
    }

    /// <summary>
    /// Records the operations written, so a test can assert that a skipped backup left a trail rather
    /// than only a log line. The connection-bound overloads are unused here — the Reset endpoint writes
    /// through the connectionless one — but must exist to satisfy the interface.
    /// </summary>
    private sealed class RecordingAuditWriter : IAuditEntryWriter
    {
        public List<string> Operations { get; } = [];

        public Task WriteAsync(AuditEntryEntity entry)
        {
            Operations.Add(entry.Operation);
            return Task.CompletedTask;
        }

        public Task WriteAsync(AuditEntryEntity entry, IDbConnection connection, IDbTransaction? transaction = null)
            => WriteAsync(entry);

        public Task WriteAsync(IReadOnlyList<AuditEntryEntity> entries, IDbConnection connection, IDbTransaction? transaction = null)
        {
            foreach (AuditEntryEntity entry in entries)
                Operations.Add(entry.Operation);
            return Task.CompletedTask;
        }

        public Task ClearAsync(string? table = null) => Task.CompletedTask;
    }

    /// <summary>
    /// POST /admin/database/reset calls DismissByTriggerAsync(DatabaseReset) as part of its own
    /// success path (#278) — verified via a spy writer rather than a real Reset round-trip, since a
    /// real Reset wipes System_Notification entirely (no protected/excluded table set), which would
    /// make the notification disappear regardless of whether this call ever happened.
    /// </summary>
    [TestMethod]
    public async Task ResetDatabase_CorrectKey_CallsDismissByTriggerWithDatabaseReset()
    {
        FakeNotificationWriter notificationWriter = new FakeNotificationWriter();
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, notificationWriter: notificationWriter);
        HttpResponseMessage response = await CreateClientWithKey(factory).PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.HasCount(1, notificationWriter.DismissByTriggerCalls);
        Assert.AreEqual(Quotinator.Data.Enums.NotificationDismissTrigger.DatabaseReset, notificationWriter.DismissByTriggerCalls[0]);
    }

    /// <summary>POST /admin/database/reset with no query parameter defaults preserveSchemaVersion to false (#141).</summary>
    [TestMethod]
    public async Task ResetDatabase_NoQueryParam_DefaultsPreserveSchemaVersionFalse()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer();
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy);
        HttpResponseMessage response = await CreateClientWithKey(factory).PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(spy.LastPreserveSchemaVersion);
    }

    /// <summary>POST /admin/database/reset?preserveSchemaVersion=true threads the flag through to ResetAsync (#141).</summary>
    [TestMethod]
    public async Task ResetDatabase_PreserveSchemaVersionTrue_Returns200AndPassesFlagThrough()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer();
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy);
        HttpResponseMessage response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset?preserveSchemaVersion=true", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(spy.LastPreserveSchemaVersion);
    }

    private sealed class SpyDatabaseInitializer : IDatabaseInitializer
    {
        public bool? LastPreserveSchemaVersion { get; private set; }

        /// <summary>#348 — set to make the spy refuse a reset, as a real initializer would when no backup can be taken.</summary>
        public BackupOutcome? RefuseWith { get; init; }

        /// <summary>#348 — refuse even when the override is passed, as a read-only /data genuinely does.</summary>
        public bool RefuseEvenWithOverride { get; init; }

        /// <summary>#348 — whether the reset actually ran, so a test can assert a refusal rebuilt nothing.</summary>
        public bool ResetRan { get; private set; }

        /// <summary>#348 — what the endpoint forwarded as the override, so a test can assert it is threaded through.</summary>
        public bool? LastAllowNoBackup { get; private set; }

        public int    SchemaVersion    => 5;
        public int    DataSchemaVersion => 2;
        public int    QuoteCount       => 0;
        public int    SourceCount      => 0;
        public int    CharacterCount   => 0;
        public int    PeopleCount      => 0;
        public int    SeriesCount      => 0;
        public int    UniverseCount    => 0;
        public int    StageDirectionCount => 0;
        public int    SoundCueCount    => 0;
        public int    ConversationCount => 0;
        public string? MigrationApplied => null;
        public bool   SchemaVersionOvershootDetected => false;
        public IReadOnlyList<FileImportReport> LastSeedReport => [];

        public Task<DatabaseOperationResult> InitialiseAsync() => Task.FromResult(DatabaseOperationResult.Success());

        public BackupOutcome CheckBackupReadiness(bool allowReserve = false) => BackupOutcome.Succeeded;
        public Task ReseedAsync(bool forceSourceRefresh = false) => Task.CompletedTask;

        public Task<DatabaseOperationResult> ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false)
        {
            LastPreserveSchemaVersion = preserveSchemaVersion;
            LastAllowNoBackup         = allowNoBackup;

            // Mirrors the real initializer: the override is what turns a refusal into a run, so a spy
            // that refused regardless would make the override untestable at this layer.
            if (RefuseWith is not null && (!allowNoBackup || RefuseEvenWithOverride))
                return Task.FromResult(DatabaseOperationResult.RefusedForBackup(RefuseWith.Value));

            ResetRan = true;
            return Task.FromResult(DatabaseOperationResult.Success(backupSkippedByOverride: RefuseWith is not null));
        }

        public Task<SeedPreviewResult> PreviewSeedAsync()
            => Task.FromResult(new SeedPreviewResult([], []));

        public Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false)
            => Task.FromResult(new SourceCacheResolution([], []));
    }

    public TestContext TestContext { get; set; }
}
