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
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>Endpoint tests for <c>/api/v1/import/rules/conflict</c> (#153).</summary>
[TestClass]
public class ImportRuleEndpointsTests
{
    private const string TestKey = "test-admin-key";

    private string _tempDir = null!;
    private string _bundledDir = null!;
    private string _overrideDir = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir     = Directory.CreateTempSubdirectory("quotinator_rule_endpoint_test_").FullName;
        _bundledDir  = Path.Combine(_tempDir, "bundled");
        _overrideDir = Path.Combine(_tempDir, "override");
        Directory.CreateDirectory(_bundledDir);
        Directory.CreateDirectory(_overrideDir);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private WebApplicationFactory<Program> CreateFactory(
        FakeImportActionService? actionService = null,
        FakeSourceFileOverrideRegistry? registry = null,
        IEnumerable<Source>? sources = null,
        string? adminApiKey = TestKey)
    {
        var pathResolver = new RuleFileOverridePathResolver(_overrideDir, Path.Combine(_tempDir, "override-external"), _bundledDir, Path.Combine(_tempDir, "bundled-external"));

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
                services.AddSingleton<IImportActionService>(actionService ?? new FakeImportActionService());
                services.AddSingleton<ISourceFileOverrideRegistry>(registry ?? new FakeSourceFileOverrideRegistry());
                services.AddSingleton<IRuleFileOverridePathResolver>(pathResolver);
                services.AddSingleton<IListableRepository<Source>>(new FakeSourceRepository(sources));
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
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);
        return client;
    }

    private void WriteBundledRuleFile(string fileName, string content)
        => File.WriteAllText(Path.Combine(_bundledDir, fileName), content);

    private const string SampleRuleFile =
        """{"rules":[{"entityId":"11111111-1111-1111-1111-111111111111","existingRecord":{"date":"1990"},"incomingRecord":{"date":"1991"},"fields":[{"field":"date","resolution":"Keep"}]}]}""";

    private static Source NewSource(string title, QuoteType type = QuoteType.Movie) => new()
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
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/rules/conflict?origin=Bundled");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetConflictRuleFile_InvalidOrigin_Returns422()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=NotARealOrigin");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetConflictRuleFile_NeitherBundledNorOverrideExists_Returns404()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/rules/conflict?fileName=does-not-exist.json&origin=Bundled");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetConflictRuleFile_BundledFileExists_ReturnsRulesWithOverrideFalse()
    {
        WriteBundledRuleFile("rules.json", SampleRuleFile);
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=Bundled");
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

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

        var registry = new FakeSourceFileOverrideRegistry();
        await registry.RegisterAsync("rules.json", SeedBatchOrigin.Bundled, EffectiveRuleFileResolver.ComputeContentHash(overrideContent), sourceBatchId: null);

        using var factory = CreateFactory(registry: registry);
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=Bundled");
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(doc.RootElement.GetProperty("isOverrideActive").GetBoolean());
        Assert.AreEqual("22222222-2222-2222-2222-222222222222", doc.RootElement.GetProperty("rules")[0].GetProperty("entityId").GetString());
    }

    // ── POST /conflict/generate ────────────────────────────────────────────

    [TestMethod]
    public async Task GenerateConflictRuleFile_NoApiKey_Returns401()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/import/rules/conflict/generate?fileName=rules.json&origin=Bundled&batchId=b1", content: null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GenerateConflictRuleFile_MissingBatchId_Returns422()
    {
        using var factory = CreateFactory();
        using var client  = CreateAuthorizedClient(factory);

        var response = await client.PostAsync("/api/v1/import/rules/conflict/generate?fileName=rules.json&origin=Bundled", content: null);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GenerateConflictRuleFile_ValidBatch_WritesRegisteredOverrideWithNewRule()
    {
        var fakeService = new FakeImportActionService
        {
            ReturnExportRows =
            [
                new ImportActionFieldRow
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
        var registry = new FakeSourceFileOverrideRegistry();
        using var factory = CreateFactory(fakeService, registry);
        using var client  = CreateAuthorizedClient(factory);

        var response = await client.PostAsync("/api/v1/import/rules/conflict/generate?fileName=rules.json&origin=Bundled&batchId=my-batch", content: null);
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(doc.RootElement.GetProperty("isOverrideActive").GetBoolean());
        Assert.AreEqual(1, doc.RootElement.GetProperty("rulesAdded").GetInt32());
        Assert.AreEqual(1, doc.RootElement.GetProperty("rules").GetArrayLength());

        var registered = await registry.FindAsync("rules.json", SeedBatchOrigin.Bundled);
        Assert.IsNotNull(registered, "the generate call must register the new override");
        Assert.AreEqual("my-batch", registered.SourceBatchId);

        var writtenPath = Path.Combine(_overrideDir, "rules.json");
        Assert.IsTrue(File.Exists(writtenPath), "the generate call must write the override file to disk");
    }

    [TestMethod]
    public async Task GenerateConflictRuleFile_ExistingBundledRules_AreMergedNotDropped()
    {
        // The bundled file already has a hand-authored rule for entity 1; generating from a batch
        // covering only entity 2 must not lose entity 1's rule from the resulting override — this is
        // the exact correctness gap EffectiveRuleFileResolver exists to close (see its own doc comment).
        WriteBundledRuleFile("rules.json", SampleRuleFile);

        var fakeService = new FakeImportActionService
        {
            ReturnExportRows =
            [
                new ImportActionFieldRow
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
        using var factory = CreateFactory(fakeService);
        using var client  = CreateAuthorizedClient(factory);

        var response = await client.PostAsync("/api/v1/import/rules/conflict/generate?fileName=rules.json&origin=Bundled&batchId=my-batch", content: null);
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var entityIds = doc.RootElement.GetProperty("rules").EnumerateArray()
            .Select(r => r.GetProperty("entityId").GetString())
            .ToList();
        CollectionAssert.Contains(entityIds, "11111111-1111-1111-1111-111111111111", "the pre-existing bundled rule must survive the merge");
        CollectionAssert.Contains(entityIds, "44444444-4444-4444-4444-444444444444", "the newly generated rule must be included");
    }

    // ── DELETE /conflict ───────────────────────────────────────────────────

    [TestMethod]
    public async Task RemoveOverride_NoApiKey_Returns401()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.DeleteAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=Bundled");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task RemoveOverride_NotRegistered_Returns404()
    {
        using var factory = CreateFactory();
        using var client  = CreateAuthorizedClient(factory);

        var response = await client.DeleteAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=Bundled");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task RemoveOverride_Registered_Returns204AndSubsequentGetFallsBackToBundled()
    {
        WriteBundledRuleFile("rules.json", SampleRuleFile);
        const string overrideContent =
            """{"rules":[{"entityId":"55555555-5555-5555-5555-555555555555","existingRecord":{},"incomingRecord":{},"fields":[{"field":"date","resolution":"Replace"}]}]}""";
        File.WriteAllText(Path.Combine(_overrideDir, "rules.json"), overrideContent);

        var registry = new FakeSourceFileOverrideRegistry();
        await registry.RegisterAsync("rules.json", SeedBatchOrigin.Bundled, EffectiveRuleFileResolver.ComputeContentHash(overrideContent), sourceBatchId: null);

        using var factory = CreateFactory(registry: registry);
        using var client  = CreateAuthorizedClient(factory);

        var deleteResponse = await client.DeleteAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=Bundled");
        Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/v1/import/rules/conflict?fileName=rules.json&origin=Bundled");
        var doc         = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());

        Assert.IsFalse(doc.RootElement.GetProperty("isOverrideActive").GetBoolean(), "removing the registration must fall back to the bundled copy");
        Assert.AreEqual("11111111-1111-1111-1111-111111111111", doc.RootElement.GetProperty("rules")[0].GetProperty("entityId").GetString());
    }

    // ── GET /alias ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetSourceAliasCandidates_MissingFileName_Returns422()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/rules/alias?origin=Bundled");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetSourceAliasCandidates_NoApiKeyRequired_Returns200()
    {
        using var factory = CreateFactory(sources: [NewSource("Casablanca")]);
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/rules/alias?fileName=aliases.json&origin=Bundled");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetSourceAliasCandidates_NearDuplicateTitles_SurfacedAsCandidate()
    {
        using var factory = CreateFactory(sources:
        [
            NewSource("Airplane!"),
            NewSource("Airplane"),
        ]);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/rules/alias?fileName=aliases.json&origin=Bundled");
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, doc.RootElement.GetProperty("candidates").GetArrayLength());
    }

    [TestMethod]
    public async Task GetSourceAliasCandidates_NoDuplicates_ReturnsEmptyCandidates()
    {
        using var factory = CreateFactory(sources:
        [
            NewSource("Jurassic Park"),
            NewSource("Casablanca"),
        ]);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/rules/alias?fileName=aliases.json&origin=Bundled");
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(0, doc.RootElement.GetProperty("candidates").GetArrayLength());
    }

    [TestMethod]
    public async Task GetSourceAliasCandidates_AlreadyCoveredByExistingAlias_NotReSuggested()
    {
        WriteBundledRuleFile("aliases.json",
            """{"aliases":[{"title":"Airplane","type":"Movie","canonicalTitle":"Airplane!","canonicalType":"Movie"}]}""");

        using var factory = CreateFactory(sources:
        [
            NewSource("Airplane!"),
            NewSource("Airplane"),
        ]);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/rules/alias?fileName=aliases.json&origin=Bundled");
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(0, doc.RootElement.GetProperty("candidates").GetArrayLength(), "an already-aliased pair must not be re-suggested");
    }

    [TestMethod]
    public async Task GetSourceAliasCandidates_NeverWritesToAliasFile()
    {
        var bundledPath = Path.Combine(_bundledDir, "aliases.json");
        WriteBundledRuleFile("aliases.json", """{"aliases":[]}""");
        var beforeContent = await File.ReadAllTextAsync(bundledPath);

        using var factory = CreateFactory(sources:
        [
            NewSource("Airplane!"),
            NewSource("Airplane"),
        ]);
        using var client = factory.CreateClient();

        await client.GetAsync("/api/v1/import/rules/alias?fileName=aliases.json&origin=Bundled");

        Assert.AreEqual(beforeContent, await File.ReadAllTextAsync(bundledPath), "GET must never modify the alias file on disk");
        Assert.IsFalse(File.Exists(Path.Combine(_overrideDir, "aliases.json")), "GET must never create an override file either");
    }
}
