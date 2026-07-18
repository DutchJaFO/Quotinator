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
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
    public async Task PageParam_OnLiveSpec_PublishesIntegerType(string path, string paramName)
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var doc = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json");

        var parameter = doc!.RootElement
            .GetProperty("paths").GetProperty(path)
            .GetProperty("get").GetProperty("parameters")
            .EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == paramName);

        var typeProperty = parameter.GetProperty("schema").GetProperty("type");
        var types = typeProperty.ValueKind == JsonValueKind.Array
            ? typeProperty.EnumerateArray().Select(t => t.GetString()).ToList()
            : [typeProperty.GetString()];

        CollectionAssert.Contains(types, "integer", $"{paramName} on {path} must publish an integer type on the live spec, not string");
    }
}
