using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Constants.Api;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.OpenApi;

/// <summary>
/// Fetches the real, live <c>/openapi/v1.json</c> through the full HTTP pipeline. This is deliberately
/// separate from <c>NumericParameterSchemaTransformerTests</c>, which exercises the transformer class
/// directly against a synthetic <c>OpenApiOperation</c> and would keep passing even if the transformer
/// were never actually registered via <c>AddOpenApi</c> in <c>Program.cs</c> — only a request through
/// the real pipeline proves the DI wiring itself. Written to replace a <c>curl | grep</c> check of the
/// live spec from #195's own T2 pass with a deterministic, repeatable assertion.
/// </summary>
[TestClass]
public class OpenApiSpecEndpointTests
{
    private static WebApplicationFactory<Program> CreateFactory() =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
            }));

    [TestMethod]
    [DataRow("/api/v1/quotes", "page")]
    [DataRow("/api/v1/quotes", "pageSize")]
    [DataRow("/api/v1/admin/audit", "page")]
    [DataRow("/api/v1/admin/audit", "pageSize")]
    [DataRow("/api/v1/import/actions", "page")]
    [DataRow("/api/v1/import/actions", "pageSize")]
    [DataRow("/api/v1/masterdata/sources", "page")]
    [DataRow("/api/v1/masterdata/sources", "pageSize")]
    [DataRow("/api/v1/masterdata/characters", "page")]
    [DataRow("/api/v1/masterdata/characters", "pageSize")]
    [DataRow("/api/v1/masterdata/people", "page")]
    [DataRow("/api/v1/masterdata/people", "pageSize")]
    [DataRow("/api/v1/masterdata/series", "page")]
    [DataRow("/api/v1/masterdata/series", "pageSize")]
    [DataRow("/api/v1/conversations", "page")]
    [DataRow("/api/v1/conversations", "pageSize")]
    [DataRow("/api/v1/masterdata/stagedirections", "page")]
    [DataRow("/api/v1/masterdata/stagedirections", "pageSize")]
    [DataRow("/api/v1/masterdata/soundcues", "page")]
    [DataRow("/api/v1/masterdata/soundcues", "pageSize")]
    [DataRow("/api/v1/notifications", "page")]
    [DataRow("/api/v1/notifications", "pageSize")]
    public async Task PageParam_OnLiveSpec_PublishesIntegerType(string path, string paramName)
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        JsonDocument? doc = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json", TestContext.CancellationToken);

        JsonElement parameter = doc!.RootElement
            .GetProperty("paths").GetProperty(path)
            .GetProperty("get").GetProperty("parameters")
            .EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == paramName);

        JsonElement typeProperty = parameter.GetProperty("schema").GetProperty("type");
        List<string?> types = typeProperty.ValueKind == JsonValueKind.Array
            ? [.. typeProperty.EnumerateArray().Select(t => t.GetString())]
            : [typeProperty.GetString()];

        Assert.Contains("integer", types, $"{paramName} on {path} must publish an integer type on the live spec, not string");
    }

    /// <summary>
    /// Every tag an endpoint carries is declared at the document's top level with a description, so no
    /// group renders in Scalar without one.
    /// </summary>
    /// <remarks>
    /// ADR 020. `Notifications` was missing for two releases: the constant existed, the endpoints
    /// carried it, and the group rendered — just with no description and no ordering, because only the
    /// six declared tags have either. Nothing failed, which is why nobody noticed; found by reading the
    /// live spec during #339.
    ///
    /// Asserted against the operations' own tags rather than a list written here, so a tag added to an
    /// endpoint and nowhere else fails on its own instead of waiting for someone to update this test —
    /// a maintained list would reproduce the same manual step that failed, one layer down.
    ///
    /// An entry declared with an empty description is treated as missing, because it renders that way.
    /// </remarks>
    [TestMethod]
    public async Task EveryTagAnEndpointUses_IsDeclaredWithADescription()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        JsonDocument? doc = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json", TestContext.CancellationToken);

        HashSet<string> usedByOperations =
        [
            .. doc!.RootElement.GetProperty("paths").EnumerateObject()
                .SelectMany(path => path.Value.EnumerateObject())
                .Where(operation => operation.Value.TryGetProperty("tags", out _))
                .SelectMany(operation => operation.Value.GetProperty("tags").EnumerateArray())
                .Select(tag => tag.GetString()!)
        ];

        Assert.IsNotEmpty(usedByOperations, "No operation in the live spec carries a tag at all.");

        Dictionary<string, string?> declared = doc.RootElement.TryGetProperty("tags", out JsonElement tags)
            ? tags.EnumerateArray().ToDictionary(
                t => t.GetProperty("name").GetString()!,
                t => t.TryGetProperty("description", out JsonElement d) ? d.GetString() : null)
            : [];

        List<string> failures =
        [
            .. usedByOperations.Order().Select(tag => !declared.ContainsKey(tag)
                    ? $"{tag} — used by an operation but not declared in the document's top-level tags"
                    : string.IsNullOrWhiteSpace(declared[tag])
                        ? $"{tag} — declared but carries no description"
                        : null)
                .OfType<string>()
        ];

        Assert.IsEmpty(failures,
            "Every tag an endpoint uses is declared with a description, or its group renders without one:\n"
            + string.Join("\n", failures));
    }

    /// <summary>
    /// The five backup routes carry <c>Backup</c>, not <c>Admin</c> (#349).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="EveryTagAnEndpointUses_IsDeclaredWithADescription"/> because that test
    /// stays green if the tag is never introduced at all — it only checks that whatever tags exist are
    /// declared. This one checks the deliberate departure from the group-level default actually
    /// happened, which is the part a later reader is most likely to "correct" back.
    /// </remarks>
    [TestMethod]
    public async Task BackupRoutes_AreTaggedBackup_NotAdmin()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        JsonDocument? doc = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json", TestContext.CancellationToken);

        string[] backupPaths =
        [
            .. doc!.RootElement.GetProperty("paths").EnumerateObject()
                 .Select(p => p.Name)
                 .Where(p => p.StartsWith("/api/v1/admin/backups", StringComparison.OrdinalIgnoreCase))
        ];

        Assert.HasCount(5, backupPaths,
            "expected the list, status, create, content and delete paths:\n" + string.Join("\n", backupPaths));

        List<string> failures = [];
        foreach (JsonProperty path in doc.RootElement.GetProperty("paths").EnumerateObject()
                                        .Where(p => backupPaths.Contains(p.Name)))
        {
            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                string[] tags = [.. operation.Value.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!)];

                if (!tags.Contains(ApiTags.Backup))
                    failures.Add($"{operation.Name.ToUpperInvariant()} {path.Name} — tagged {string.Join(", ", tags)}, not {ApiTags.Backup}");
            }
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    public TestContext TestContext { get; set; }
}
