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

    /// <summary>GET /admin/database/seed/preview is publicly accessible: no API key required.</summary>
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
    /// Before #348 this state produced an unhandled 500: the state the /health reason told the operator to
    /// resolve by resetting.
    /// </summary>
    [TestMethod]
    public async Task ResetDatabase_WhenNoBackupCanBeTaken_RefusesWithAStatedFailureRatherThanAnUnhandled500()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer { RefuseWith = BackupOutcome.SourceUnreadable };
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy);

        HttpResponseMessage response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// #304: a reseed resolves the recommendation however it was triggered, so the plain admin endpoint
    /// dismisses it too, not only the notification action. Without this the recommendation stays active
    /// after the operator resolved it by hand, and then silently dedupes every later occurrence.
    /// </summary>
    [TestMethod]
    public async Task Reseed_Success_DismissesReseedRecommendation()
    {
        FakeNotificationWriter writer = new();
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, notificationWriter: writer);

        HttpResponseMessage response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reseed", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(NotificationDismissTrigger.Reseed, writer.DismissByTriggerCalls,
            "A reseed through the plain endpoint resolves the same condition the notification action does.");
    }

    /// <summary>
    /// #304 trigger 2: Reset deliberately does not reimport bundled content (#156), so a successful
    /// Reset leaves the database with no quotes and nothing tells the operator. It writes an
    /// ActionRequired recommendation instead of reseeding on their behalf.
    /// </summary>
    [TestMethod]
    public async Task Reset_Success_WritesReseedRecommendation()
    {
        FakeNotificationWriter writer = new();
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, notificationWriter: writer);

        HttpResponseMessage response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.ContainsSingle(
            writer.WrittenMetadata.Where(m => m.Kind == NotificationMetadataKind.ReseedRecommended),
            "A successful Reset must recommend a reseed exactly once: the database now holds no quote content.");
    }

    /// <summary>
    /// A Reset that refused to run leaves the database untouched, so there is nothing to recommend:
    /// the positive control's counterpart, and the case a producer bolted on after the fact would miss.
    /// </summary>
    [TestMethod]
    public async Task Reset_WhenRefused_WritesNoReseedRecommendation()
    {
        FakeNotificationWriter writer = new();
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer { RefuseWith = BackupOutcome.BudgetExceeded };
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy, writer);

        await CreateClientWithKey(factory).PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);

        Assert.IsEmpty(
            writer.WrittenMetadata.Where(m => m.Kind == NotificationMetadataKind.ReseedRecommended),
            "The reset never ran, so the content is still there and recommending a reseed would be wrong.");
    }

    [TestMethod]
    public async Task ResetDatabase_WhenNoBackupCanBeTaken_NamesTheObstacle()
    {
        JsonElement body = await RefusedResetBodyAsync(new SpyDatabaseInitializer { RefuseWith = BackupOutcome.BudgetExceeded });

        Assert.AreEqual(nameof(BackupOutcome.BudgetExceeded), body.GetProperty("backupObstacle").GetString());
    }

    [TestMethod]
    public async Task ResetDatabase_WhenNoBackupCanBeTaken_DescribesTheCause()
    {
        JsonElement body = await RefusedResetBodyAsync(new SpyDatabaseInitializer { RefuseWith = BackupOutcome.BudgetExceeded });

        Assert.Contains("quota", body.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An error that names no way out is not actionable.</summary>
    [TestMethod]
    public async Task ResetDatabase_WhenNoBackupCanBeTaken_OffersARemedy()
    {
        JsonElement body = await RefusedResetBodyAsync(new SpyDatabaseInitializer { RefuseWith = BackupOutcome.BudgetExceeded });

        Assert.IsGreaterThan(0, body.GetProperty("remedies").GetArrayLength());
    }

    /// <summary>For a full quota the override is a remedy the caller can act on immediately.</summary>
    [TestMethod]
    public async Task ResetDatabase_WhenTheQuotaIsFull_OffersTheOverride()
    {
        JsonElement body = await RefusedResetBodyAsync(new SpyDatabaseInitializer { RefuseWith = BackupOutcome.BudgetExceeded });

        Assert.Contains("allowNoBackup", AllRemedies(body), StringComparison.Ordinal);
    }

    /// <summary>
    /// Found live: a corrupt database passes the pre-flight, which inspects storage and never the database,
    /// and then fails inside the table drop. The override cannot rescue that, since there is nothing to drop,
    /// so offering it would name a remedy that cannot succeed: the defect #326 fixed for the data directory.
    /// </summary>
    [TestMethod]
    public async Task ResetDatabase_WhenTheSourceIsUnreadable_DoesNotOfferTheOverride()
    {
        JsonElement body = await RefusedResetBodyAsync(new SpyDatabaseInitializer { RefuseWith = BackupOutcome.SourceUnreadable });

        Assert.DoesNotContain("allowNoBackup", AllRemedies(body), StringComparison.Ordinal);
    }

    /// <summary>The remedy that does work for an unreadable source is replacing the file from outside the application.</summary>
    [TestMethod]
    public async Task ResetDatabase_WhenTheSourceIsUnreadable_OffersReplacingTheFile()
    {
        JsonElement body = await RefusedResetBodyAsync(new SpyDatabaseInitializer { RefuseWith = BackupOutcome.SourceUnreadable });

        Assert.Contains("restart", AllRemedies(body), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Found live on a read-only <c>/data</c>: the override was offered, used, still refused, and the
    /// response then repeated it. Advice this very request disproved sends the operator round the same loop.
    /// </summary>
    [TestMethod]
    public async Task ResetDatabase_WhenTheOverrideWasTriedAndStillRefused_DoesNotOfferItAgain()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer
        {
            RefuseWith = BackupOutcome.DestinationFileNotWritable,
            RefuseEvenWithOverride = true,
        };

        JsonElement body = await RefusedResetBodyAsync(spy, "?allowNoBackup=true");

        Assert.DoesNotContain("allowNoBackup", AllRemedies(body), StringComparison.Ordinal);
    }

    /// <summary>The endpoint must actually forward the override, not just accept it.</summary>
    [TestMethod]
    public async Task ResetDatabase_WithOverride_ForwardsTheOverride()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer { RefuseWith = BackupOutcome.BudgetExceeded };
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy);

        await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset?allowNoBackup=true", null, TestContext.CancellationToken);

        Assert.IsTrue(spy.LastAllowNoBackup);
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
    /// Posts a reset the spy refuses and returns the problem body. The 409 is a precondition, asserted with
    /// its own message, so a test failing here is traceable to the refusal not happening rather than to the
    /// statement the test makes about the body.
    /// </summary>
    private async Task<JsonElement> RefusedResetBodyAsync(SpyDatabaseInitializer spy, string query = "")
    {
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy);

        HttpResponseMessage response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset" + query, null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, "precondition: the reset was refused with a 409");
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        return doc.RootElement.Clone();
    }

    private static string AllRemedies(JsonElement body) =>
        string.Join(" ", body.GetProperty("remedies").EnumerateArray().Select(r => r.GetString()!));

    /// <summary>
    /// Records the operations written, so a test can assert that a skipped backup left a trail rather
    /// than only a log line. The connection-bound overloads are unused here (the Reset endpoint writes
    /// through the connectionless one) but must exist to satisfy the interface.
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
    /// success path (#278): verified via a spy writer rather than a real Reset round-trip, since a
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

    /// <summary>
    /// #349: a refused reset's remedies name the endpoints that can actually resolve it, rather than
    /// describing an action the operator has no route to perform.
    /// </summary>
    [TestMethod]
    public async Task ResetRefusedForBudget_RemedyNamesTheBackupEndpoints()
    {
        SpyDatabaseInitializer spy = new SpyDatabaseInitializer { RefuseWith = BackupOutcome.BudgetExceeded };
        using WebApplicationFactory<Program> factory = CreateFactory(TestKey, spy);

        HttpResponseMessage response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);
        Assert.Contains("/api/v1/admin/backups", body, StringComparison.Ordinal);
    }

    private sealed class SpyDatabaseInitializer : IDatabaseInitializer
    {
        public bool? LastPreserveSchemaVersion { get; private set; }

        /// <summary>#348: set to make the spy refuse a reset, as a real initializer would when no backup can be taken.</summary>
        public BackupOutcome? RefuseWith { get; init; }

        /// <summary>#348: refuse even when the override is passed, as a read-only /data genuinely does.</summary>
        public bool RefuseEvenWithOverride { get; init; }

        /// <summary>#348: whether the reset actually ran, so a test can assert a refusal rebuilt nothing.</summary>
        public bool ResetRan { get; private set; }

        /// <summary>#348: what the endpoint forwarded as the override, so a test can assert it is threaded through.</summary>
        public bool? LastAllowNoBackup { get; private set; }

        public int    SchemaVersion    => 5;
        public int    DataSchemaVersion => 2;
        public int    QuoteCount       => 0;
        public int    SourceCount      => 0;
        public int    CharacterCount   => 0;
        public int    PeopleCount      => 0;
        public int    SeriesCount      => 0;
        public int    SeasonCount      => 0;
        public int    UniverseCount    => 0;
        public int    StageDirectionCount => 0;
        public int    SoundCueCount    => 0;
        public int    ConversationCount => 0;
        public string? MigrationApplied => null;
        public bool   SchemaVersionOvershootDetected => false;
        public IReadOnlyList<FileImportReport> LastSeedReport => [];

        public Task<DatabaseOperationResult> InitialiseAsync() => Task.FromResult(DatabaseOperationResult.Success());

        public BackupOutcome CheckBackupReadiness(bool allowReserve = false) => BackupOutcome.Succeeded;
        public Task<DatabaseBackupResult> CreateBackupAsync() => Task.FromResult(DatabaseBackupResult.Success("spy-backup.db"));
        public Task ReseedAsync(bool forceSourceRefresh = false) => Task.CompletedTask;

        public Task<DatabaseOperationResult> ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false)
        {
            LastPreserveSchemaVersion = preserveSchemaVersion;
            LastAllowNoBackup         = allowNoBackup;

            // Mirrors the real initializer: the override is what turns a refusal into a run, so a spy
            // that refused regardless would make the override untestable at this layer.
            if (RefuseWith is not null && (!allowNoBackup || RefuseEvenWithOverride))
                return Task.FromResult(DatabaseOperationResult.RefusedForBackup(RefuseWith.Value, BackupGuardedStep.Reset));

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
