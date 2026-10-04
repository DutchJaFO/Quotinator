using Quotinator.Core.Enums;
using Quotinator.Data.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Entities;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Paths;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.Database;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Data.Entities;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>Endpoint tests for <c>/api/v1/import/rules/conflict</c> (#153).</summary>
[TestClass]
public class ImportRuleEndpointsTests
{
    private const string TestKey = "test-admin-key";

    private TempDirectory _tempDir = null!;
    private string _bundledDir = null!;
    private string _overrideDir = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir     = new TempDirectory("quotinator_rule_endpoint_test_");
        _bundledDir  = Path.Combine(_tempDir.Path, "bundled");
        _overrideDir = Path.Combine(_tempDir.Path, "override");
        Directory.CreateDirectory(_bundledDir);
        Directory.CreateDirectory(_overrideDir);
    }

    [TestCleanup]
    public void TestCleanup() => _tempDir.Dispose();

    private WebApplicationFactory<Program> CreateFactory(
        FakeImportActionService? actionService = null,
        FakeSourceFileOverrideRegistry? registry = null,
        IEnumerable<SourceEntity>? sources = null,
        string? adminApiKey = TestKey)
    {
        RuleFileOverridePathResolver pathResolver = new(_overrideDir, Path.Combine(_tempDir.Path, "override-external"), _bundledDir, Path.Combine(_tempDir.Path, "bundled-external"));

        return new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
                services.AddSingleton<IImportActionService>(actionService ?? new FakeImportActionService());
                services.AddSingleton<ISourceFileOverrideRegistry>(registry ?? new FakeSourceFileOverrideRegistry());
                services.AddSingleton<IRuleFileOverridePathResolver>(pathResolver);
                services.AddSingleton<IListableRepository<SourceEntity>>(new FakeSourceRepository(sources));
            });
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Quotinator:AdminApiKey"] = adminApiKey
                });
            });
        });
    }

    private static HttpClient CreateAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);
        return client;
    }

    private void WriteBundledRuleFile(string fileName, string content)
        => File.WriteAllText(Path.Combine(_bundledDir, fileName), content);

    private const string SampleRuleFile =
        """{"rules":[{"entityId":"11111111-1111-1111-1111-111111111111","existingRecord":{"date":"1990"},"incomingRecord":{"date":"1991"},"fields":[{"field":"date","resolution":"Keep"}]}]}""";

    private static SourceEntity NewSource(string title, QuoteType type = QuoteType.Movie) => new()
    {
        Id          = Guid.NewGuid(),
        Title       = title,
        Type        = new SafeValue<QuoteType?>(type.ToString(), type),
        DateCreated = SafeDateValue.Now,
    };

    // ── GET /conflict ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetConflictRuleFile_MissingFileName_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/rules/conflict?origin=Bundled", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetConflictRuleFile_InvalidOrigin_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=NotARealOrigin", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetConflictRuleFile_NeitherBundledNorOverrideExists_Returns404()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/rules/conflict?fileName=does-not-exist.json&origin=Bundled", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetConflictRuleFile_BundledFileExists_ReturnsRulesWithOverrideFalse()
    {
        WriteBundledRuleFile("rules.json", SampleRuleFile);
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=Bundled", TestContext.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(doc.RootElement.GetProperty("isOverrideActive").GetBoolean());
        Assert.AreEqual(1, doc.RootElement.GetProperty("rules").GetArrayLength());
    }

    [TestMethod]
    public async Task GetConflictRuleFile_RegisteredOverrideExists_ReturnsOverrideRulesWithOverrideTrue()
    {
        WriteBundledRuleFile("rules.json", SampleRuleFile);

        const string overrideContent =
            """{"rules":[{"entityId":"22222222-2222-2222-2222-222222222222","existingRecord":{"date":"2000"},"incomingRecord":{"date":"2001"},"fields":[{"field":"date","resolution":"Replace"}]}]}""";
        File.WriteAllText(Path.Combine(_overrideDir, "rules.json"), overrideContent);

        FakeSourceFileOverrideRegistry registry = new();
        await registry.RegisterAsync("rules.json", SeedBatchOrigin.Bundled, EffectiveRuleFileResolver.ComputeContentHash(overrideContent), sourceBatchId: null, TestContext.CancellationToken);

        using WebApplicationFactory<Program> factory = CreateFactory(registry: registry);
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=Bundled", TestContext.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(doc.RootElement.GetProperty("isOverrideActive").GetBoolean());
        Assert.AreEqual("22222222-2222-2222-2222-222222222222", doc.RootElement.GetProperty("rules")[0].GetProperty("entityId").GetString());
    }

    // ── POST /conflict/generate ────────────────────────────────────────────

    [TestMethod]
    public async Task GenerateConflictRuleFile_NoApiKey_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/rules/conflict/generate?fileName=rules.json&origin=Bundled&batchId=b1", content: null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GenerateConflictRuleFile_MissingBatchId_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = CreateAuthorizedClient(factory);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/rules/conflict/generate?fileName=rules.json&origin=Bundled", content: null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GenerateConflictRuleFile_ValidBatch_WritesRegisteredOverrideWithNewRule()
    {
        FakeImportActionService fakeService = new()
        {
            ReturnExportRows =
            [
                new ImportActionFieldRowResponse
                {
                    ActionId      = Guid.NewGuid(),
                    EntityId      = "33333333-3333-3333-3333-333333333333",
                    EntityType    = "Quote",
                    Field         = "date",
                    ExistingValue = "1980",
                    IncomingValue = "1981",
                    Decision      = FieldResolutionChoice.Replace,
                },
            ],
        };
        FakeSourceFileOverrideRegistry registry = new();
        using WebApplicationFactory<Program> factory = CreateFactory(fakeService, registry);
        using HttpClient client  = CreateAuthorizedClient(factory);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/rules/conflict/generate?fileName=rules.json&origin=Bundled&batchId=my-batch", content: null, TestContext.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(doc.RootElement.GetProperty("isOverrideActive").GetBoolean());
        Assert.AreEqual(1, doc.RootElement.GetProperty("rulesAdded").GetInt32());
        Assert.AreEqual(1, doc.RootElement.GetProperty("rules").GetArrayLength());

        SourceFileOverrideEntity? registered = await registry.FindAsync("rules.json", SeedBatchOrigin.Bundled, TestContext.CancellationToken);
        Assert.IsNotNull(registered, "the generate call must register the new override");
        Assert.AreEqual("my-batch", registered.SourceBatchId);

        string writtenPath = Path.Combine(_overrideDir, "rules.json");
        Assert.IsTrue(File.Exists(writtenPath), "the generate call must write the override file to disk");
    }

    [TestMethod]
    public async Task GenerateConflictRuleFile_ExistingBundledRules_AreMergedNotDropped()
    {
        // The bundled file already has a hand-authored rule for entity 1; generating from a batch
        // covering only entity 2 must not lose entity 1's rule from the resulting override — this is
        // the exact correctness gap EffectiveRuleFileResolver exists to close (see its own doc comment).
        WriteBundledRuleFile("rules.json", SampleRuleFile);

        FakeImportActionService fakeService = new()
        {
            ReturnExportRows =
            [
                new ImportActionFieldRowResponse
                {
                    ActionId      = Guid.NewGuid(),
                    EntityId      = "44444444-4444-4444-4444-444444444444",
                    EntityType    = "Quote",
                    Field         = "source",
                    ExistingValue = "Old Title",
                    IncomingValue = "New Title",
                    Decision      = FieldResolutionChoice.Replace,
                },
            ],
        };
        using WebApplicationFactory<Program> factory = CreateFactory(fakeService);
        using HttpClient client  = CreateAuthorizedClient(factory);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/rules/conflict/generate?fileName=rules.json&origin=Bundled&batchId=my-batch", content: null, TestContext.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        List<string> entityIds = [.. doc.RootElement.GetProperty("rules").EnumerateArray().Select(r => r.GetProperty("entityId").GetString() ?? string.Empty)];
        Assert.Contains("11111111-1111-1111-1111-111111111111", entityIds, "the pre-existing bundled rule must survive the merge");
        Assert.Contains("44444444-4444-4444-4444-444444444444", entityIds, "the newly generated rule must be included");
    }

    /// <summary>#420: the reported defect, at the surface it was reported on. A rule file naming one
    /// entity twice made `ConflictRuleGenerator.Merge`'s per-entity dictionary throw
    /// `ArgumentException`, which reached the client as an unhandled 500. Per ADR 022 the condition is
    /// checkable, so it is a stated 422 that names the file and the repeated id.</summary>
    [TestMethod]
    public async Task GenerateConflictRuleFile_ExistingFileNamesOneEntityTwice_Returns422()
    {
        WriteBundledRuleFile("rules.json", DuplicateEntityRuleFile);

        FakeImportActionService fakeService = new()
        {
            ReturnExportRows =
            [
                new ImportActionFieldRowResponse
                {
                    ActionId      = Guid.NewGuid(),
                    EntityId      = "44444444-4444-4444-4444-444444444444",
                    EntityType    = "Quote",
                    Field         = "source",
                    ExistingValue = "Old Title",
                    IncomingValue = "New Title",
                    Decision      = FieldResolutionChoice.Replace,
                },
            ],
        };
        using WebApplicationFactory<Program> factory = CreateFactory(fakeService);
        using HttpClient client  = CreateAuthorizedClient(factory);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/rules/conflict/generate?fileName=rules.json&origin=Bundled&batchId=my-batch", content: null, TestContext.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "A duplicate entity id is a stated outcome, never an unhandled 500");
        Assert.Contains("rules.json", body, "The response names the file so the operator knows which one to fix");
        Assert.Contains("11111111-1111-1111-1111-111111111111", body, "The response names the repeated entity id");
    }

    /// <summary>#420: a second unhandled 500 on this endpoint, reachable once `fields[]` carries a
    /// recorded value. The endpoint serializes `Merge`'s output straight back to the override file, and
    /// serializing an `Undefined` `JsonElement` throws — so a hand-authored file that legitimately omits
    /// a field's `recordedIncomingValue` would fail on write. It must round-trip as absent, never be
    /// invented as an explicit null, which would silently change that field's next outcome away from
    /// Stale.</summary>
    [TestMethod]
    public async Task GenerateConflictRuleFile_ExistingFieldHasNoRecordedValue_RoundTripsWithoutInventingNull()
    {
        WriteBundledRuleFile("rules.json", SampleRuleFile);

        // A batch adding a second field to the SAME entity, so the entry whose field has no recorded
        // value is rewritten rather than merely copied.
        FakeImportActionService fakeService = new()
        {
            ReturnExportRows =
            [
                new ImportActionFieldRowResponse
                {
                    ActionId      = Guid.NewGuid(),
                    EntityId      = "11111111-1111-1111-1111-111111111111",
                    EntityType    = "Quote",
                    Field         = "source",
                    ExistingValue = "Old Title",
                    IncomingValue = "New Title",
                    Decision      = FieldResolutionChoice.Replace,
                },
            ],
        };
        using WebApplicationFactory<Program> factory = CreateFactory(fakeService);
        using HttpClient client  = CreateAuthorizedClient(factory);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/rules/conflict/generate?fileName=rules.json&origin=Bundled&batchId=my-batch", content: null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "A field with no recorded value is permitted and must not fail the write");

        string written = await File.ReadAllTextAsync(Path.Combine(_overrideDir, "rules.json"), TestContext.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(written);
        JsonElement dateField = doc.RootElement.GetProperty("rules").EnumerateArray()
            .Single(r => r.GetProperty("entityId").GetString() == "11111111-1111-1111-1111-111111111111")
            .GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("field").GetString() == "date");

        Assert.IsFalse(dateField.TryGetProperty("recordedIncomingValue", out _),
            "A field that recorded nothing must round-trip as absent — writing an explicit null would turn 'not recorded' into 'recorded as null' and stop that field reporting Stale");
    }

    private const string DuplicateEntityRuleFile =
        """{"rules":[{"entityId":"11111111-1111-1111-1111-111111111111","existingRecord":{"date":"1990"},"incomingRecord":{"date":"1991"},"fields":[{"field":"date","resolution":"Custom","customValue":"1992","recordedIncomingValue":"1991"}]},{"entityId":"11111111-1111-1111-1111-111111111111","existingRecord":{"character":null},"incomingRecord":{"character":null},"fields":[{"field":"character","resolution":"Custom","customValue":"Someone","recordedIncomingValue":null}]}]}""";

    // ── DELETE /conflict ───────────────────────────────────────────────────

    [TestMethod]
    public async Task RemoveOverride_NoApiKey_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.DeleteAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=Bundled", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task RemoveOverride_NotRegistered_Returns404()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = CreateAuthorizedClient(factory);

        HttpResponseMessage response = await client.DeleteAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=Bundled", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task RemoveOverride_Registered_Returns204AndSubsequentGetFallsBackToBundled()
    {
        WriteBundledRuleFile("rules.json", SampleRuleFile);
        const string overrideContent =
            """{"rules":[{"entityId":"55555555-5555-5555-5555-555555555555","existingRecord":{},"incomingRecord":{},"fields":[{"field":"date","resolution":"Replace"}]}]}""";
        File.WriteAllText(Path.Combine(_overrideDir, "rules.json"), overrideContent);

        FakeSourceFileOverrideRegistry registry = new();
        await registry.RegisterAsync("rules.json", SeedBatchOrigin.Bundled, EffectiveRuleFileResolver.ComputeContentHash(overrideContent), sourceBatchId: null, TestContext.CancellationToken);

        using WebApplicationFactory<Program> factory = CreateFactory(registry: registry);
        using HttpClient client  = CreateAuthorizedClient(factory);

        HttpResponseMessage deleteResponse = await client.DeleteAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=Bundled", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage getResponse = await client.GetAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=Bundled", TestContext.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.IsFalse(doc.RootElement.GetProperty("isOverrideActive").GetBoolean(), "removing the registration must fall back to the bundled copy");
        Assert.AreEqual("11111111-1111-1111-1111-111111111111", doc.RootElement.GetProperty("rules")[0].GetProperty("entityId").GetString());
    }

    // ── GET /alias ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetSourceAliasCandidates_MissingFileName_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/rules/alias?origin=Bundled", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetSourceAliasCandidates_NoApiKeyRequired_Returns200()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(sources: [NewSource("Casablanca")]);
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/rules/alias?fileName=aliases.json&origin=Bundled", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetSourceAliasCandidates_NearDuplicateTitles_SurfacedAsCandidate()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(sources:
        [
            NewSource("Airplane!"),
            NewSource("Airplane"),
        ]);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/rules/alias?fileName=aliases.json&origin=Bundled", TestContext.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, doc.RootElement.GetProperty("candidates").GetArrayLength());
    }

    [TestMethod]
    public async Task GetSourceAliasCandidates_NoDuplicates_ReturnsEmptyCandidates()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(sources:
        [
            NewSource("Jurassic Park"),
            NewSource("Casablanca"),
        ]);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/rules/alias?fileName=aliases.json&origin=Bundled", TestContext.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(0, doc.RootElement.GetProperty("candidates").GetArrayLength());
    }

    [TestMethod]
    public async Task GetSourceAliasCandidates_AlreadyCoveredByExistingAlias_NotReSuggested()
    {
        WriteBundledRuleFile("aliases.json",
            """{"aliases":[{"title":"Airplane","type":"Movie","canonicalTitle":"Airplane!","canonicalType":"Movie"}]}""");

        using WebApplicationFactory<Program> factory = CreateFactory(sources:
        [
            NewSource("Airplane!"),
            NewSource("Airplane"),
        ]);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/rules/alias?fileName=aliases.json&origin=Bundled", TestContext.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(0, doc.RootElement.GetProperty("candidates").GetArrayLength(), "an already-aliased pair must not be re-suggested");
    }

    [TestMethod]
    public async Task GetSourceAliasCandidates_NeverWritesToAliasFile()
    {
        string bundledPath = Path.Combine(_bundledDir, "aliases.json");
        WriteBundledRuleFile("aliases.json", """{"aliases":[]}""");
        string beforeContent = await File.ReadAllTextAsync(bundledPath, TestContext.CancellationToken);

        using WebApplicationFactory<Program> factory = CreateFactory(sources:
        [
            NewSource("Airplane!"),
            NewSource("Airplane"),
        ]);
        using HttpClient client = factory.CreateClient();

        await client.GetAsync("/api/v1/import/rules/alias?fileName=aliases.json&origin=Bundled", TestContext.CancellationToken);

        Assert.AreEqual(beforeContent, await File.ReadAllTextAsync(bundledPath, TestContext.CancellationToken), "GET must never modify the alias file on disk");
        Assert.IsFalse(File.Exists(Path.Combine(_overrideDir, "aliases.json")), "GET must never create an override file either");
    }

    public TestContext TestContext { get; set; }
}
