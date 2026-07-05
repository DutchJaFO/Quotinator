using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Quotinator.Api.OpenApi;
using Quotinator.Core.Models;

namespace Quotinator.Api.Tests.OpenApi;

[TestClass]
public class ImportModelSchemaTransformerTests
{
    // -------------------------------------------------------------------------
    #region Helpers

    // Must match this app's actual JSON naming policy (camelCase, via ASP.NET Core's web defaults)
    // — JsonSerializerOptions.Default keeps PascalCase, which would never match schema.Properties'
    // camelCase keys and silently no-op the transformer. TypeInfoResolver must be set explicitly
    // for reflection-based metadata — this options instance has no source-generated context.
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    private static OpenApiSchemaTransformerContext Context(Type type) => new()
    {
        DocumentName        = "v1",
        JsonTypeInfo        = WebOptions.GetTypeInfo(type),
        JsonPropertyInfo    = null,
        ParameterDescription = null,
        ApplicationServices = new ServiceCollection().BuildServiceProvider()
    };

    private static async Task<OpenApiSchema> Transform(Type type, IDictionary<string, IOpenApiSchema> properties)
    {
        var schema = new OpenApiSchema { Properties = properties };
        await new ImportModelSchemaTransformer().TransformAsync(schema, Context(type), CancellationToken.None);
        return schema;
    }

    private static OpenApiSchema IntSchema() => new() { Type = JsonSchemaType.Integer | JsonSchemaType.String, Pattern = "^-?(?:0|[1-9]\\d*)$" };
    private static OpenApiSchema StringSchema() => new() { Type = JsonSchemaType.String };

    #endregion

    // -------------------------------------------------------------------------
    #region Integer properties reverted to plain integer

    [TestMethod]
    public async Task ImportSummary_IntegerProperties_TypeIsIntegerWithNoPattern()
    {
        var properties = new Dictionary<string, IOpenApiSchema>
        {
            ["total"] = IntSchema(), ["imported"] = IntSchema(), ["updated"] = IntSchema(),
            ["skipped"] = IntSchema(), ["errors"] = IntSchema()
        };

        var schema = await Transform(typeof(ImportSummary), properties);

        foreach (var name in new[] { "total", "imported", "updated", "skipped", "errors" })
        {
            var property = (OpenApiSchema)schema.Properties![name];
            Assert.AreEqual(JsonSchemaType.Integer, property.Type);
            Assert.IsNull(property.Pattern);
        }
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Enum-shaped string properties get a fixed value list

    [TestMethod]
    public async Task ImportResultResponse_ConflictPolicy_GetsFiveValueEnum()
    {
        var properties = new Dictionary<string, IOpenApiSchema> { ["conflictPolicy"] = StringSchema() };

        var schema = await Transform(typeof(ImportResultResponse), properties);

        var property = (OpenApiSchema)schema.Properties!["conflictPolicy"];
        CollectionAssert.AreEqual(
            new[] { "skip", "newest-wins", "merge-ours", "merge-theirs", "review" },
            property.Enum!.Select(v => v!.GetValue<string>()).ToList());
    }

    [TestMethod]
    public async Task ImportConflictEntry_AppliedPolicy_GetsFiveValueEnum()
    {
        var properties = new Dictionary<string, IOpenApiSchema> { ["appliedPolicy"] = StringSchema() };

        var schema = await Transform(typeof(ImportConflictEntry), properties);

        var property = (OpenApiSchema)schema.Properties!["appliedPolicy"];
        CollectionAssert.AreEqual(
            new[] { "skip", "newest-wins", "merge-ours", "merge-theirs", "review" },
            property.Enum!.Select(v => v!.GetValue<string>()).ToList());
    }

    [TestMethod]
    public async Task ImportConflictEntry_Status_GetsTwoValueEnum()
    {
        var properties = new Dictionary<string, IOpenApiSchema> { ["status"] = StringSchema() };

        var schema = await Transform(typeof(ImportConflictEntry), properties);

        var property = (OpenApiSchema)schema.Properties!["status"];
        CollectionAssert.AreEqual(
            new[] { "resolved", "pending" },
            property.Enum!.Select(v => v!.GetValue<string>()).ToList());
    }

    [TestMethod]
    public async Task UnrelatedType_StringProperty_NotTurnedIntoEnum()
    {
        var properties = new Dictionary<string, IOpenApiSchema> { ["quoteId"] = StringSchema() };

        var schema = await Transform(typeof(ImportRowError), properties);

        var property = (OpenApiSchema)schema.Properties!["quoteId"];
        Assert.IsNull(property.Enum);
    }

    #endregion
}
