using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.OpenApi;

/// <summary>
/// Fetches the real, live <c>/openapi/v1.json</c> and confirms the quote/admin endpoints (#148) each
/// publish a real typed <c>$ref</c> schema for their 200 response, with documented properties — rather
/// than the bare <c>{"200": {"description": "OK"}}</c> every endpoint had before #148.
/// </summary>
[TestClass]
public class QuoteAndAdminResponseSchemaTests
{
    private static WebApplicationFactory<Program> CreateFactory() =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
            }));

    [TestMethod]
    [DataRow("/api/v1/quotes/random", "get")]
    [DataRow("/api/v1/quotes/search", "get")]
    [DataRow("/api/v1/quotes/{id}", "get")]
    [DataRow("/api/v1/quotes", "get")]
    [DataRow("/api/v1/admin/database/seed/preview", "get")]
    [DataRow("/api/v1/admin/database/reseed", "post")]
    [DataRow("/api/v1/admin/database/reset", "post")]
    [DataRow("/api/v1/admin/sources/refresh", "post")]
    [DataRow("/api/v1/admin/audit", "get")]
    public async Task SuccessResponse_OnLiveSpec_HasRealSchemaRefWithDocumentedProperties(string path, string method)
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var doc  = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json", TestContext.CancellationToken);
        var root = doc!.RootElement;

        var response200 = root.GetProperty("paths").GetProperty(path).GetProperty(method)
            .GetProperty("responses").GetProperty("200");

        var schema = response200.GetProperty("content").GetProperty("application/json").GetProperty("schema");

        Assert.IsTrue(schema.TryGetProperty("$ref", out var refProp),
            $"{method.ToUpperInvariant()} {path}'s 200 response has no $ref — still an untyped schema");

        var resolved = ResolveRef(root, refProp.GetString()!);

        Assert.IsTrue(resolved.TryGetProperty("properties", out var properties) && properties.EnumerateObject().Any(),
            $"Schema referenced by {method.ToUpperInvariant()} {path}'s 200 response has no documented properties");
    }

    /// <summary>
    /// <see cref="Quotinator.Api.OpenApi.ImportModelSchemaTransformer"/>'s int/int? fix loops over every
    /// property of every schema unconditionally (not scoped to import types) — confirms new DTOs
    /// introduced by #148 (e.g. <c>DatabaseSeedSummaryResponse.Quotes</c>) get the same correction
    /// rather than the generator's default <c>["integer","string"]</c> union.
    /// </summary>
    [TestMethod]
    public async Task DatabaseSeedSummaryResponse_QuotesProperty_OnLiveSpec_IsPlainInteger()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var doc  = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json", TestContext.CancellationToken);
        var root = doc!.RootElement;

        var response200 = root.GetProperty("paths").GetProperty("/api/v1/admin/database/reseed").GetProperty("post")
            .GetProperty("responses").GetProperty("200");
        var refProp  = response200.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref");
        var resolved = ResolveRef(root, refProp.GetString()!);

        var quotesType = resolved.GetProperty("properties").GetProperty("quotes").GetProperty("type");

        Assert.AreEqual(JsonValueKind.String, quotesType.ValueKind, "quotes must publish a single 'integer' type, not a [\"integer\",\"string\"] union");
        Assert.AreEqual("integer", quotesType.GetString());
    }

    private static JsonElement ResolveRef(JsonElement root, string pointer)
    {
        var current = root;
        foreach (var segment in pointer.TrimStart('#', '/').Split('/'))
            current = current.GetProperty(segment);
        return current;
    }

    public TestContext TestContext { get; set; }
}
