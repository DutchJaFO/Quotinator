using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Quotinator.Api.OpenApi;

namespace Quotinator.Api.Tests.OpenApi;

[TestClass]
public class EnumParameterSchemaTransformerTests
{
    // -------------------------------------------------------------------------
    #region Helpers

    private static OpenApiParameter ScalarParam(string name) => new()
    {
        Name   = name,
        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
    };

    private static OpenApiParameter ArrayParam(string name) => new()
    {
        Name   = name,
        Schema = new OpenApiSchema
        {
            Type  = JsonSchemaType.Array,
            Items = new OpenApiSchema { Type = JsonSchemaType.String }
        }
    };

    private static OpenApiOperationTransformerContext Context(string relativePath) =>
        new()
        {
            DocumentName        = "v1",
            Description         = new ApiDescription { RelativePath = relativePath },
            ApplicationServices  = new ServiceCollection().BuildServiceProvider()
        };

    private static async Task<OpenApiSchema?> TransformAndGetSchema(OpenApiParameter param, string path)
    {
        var transformer = new EnumParameterSchemaTransformer();
        var operation    = new OpenApiOperation { Parameters = [param] };
        await transformer.TransformAsync(operation, Context(path), CancellationToken.None);
        return (operation.Parameters[0] as OpenApiParameter)?.Schema as OpenApiSchema;
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Scalar enum params patched

    [TestMethod]
    public async Task Field_OnSearch_PatchedToEnum()
    {
        var schema = await TransformAndGetSchema(ScalarParam("field"), "api/v1/quotes/search");
        CollectionAssert.AreEquivalent(new[] { "quote", "source", "character", "author" },
            schema!.Enum!.Select(v => v!.ToString()).ToArray());
    }

    [TestMethod]
    public async Task Status_OnImportActions_PatchedToEnum()
    {
        var schema = await TransformAndGetSchema(ScalarParam("status"), "api/v1/import/actions");
        CollectionAssert.AreEquivalent(new[] { "Pending", "Decided", "Applied", "Discarded" },
            schema!.Enum!.Select(v => v!.ToString()).ToArray());
    }

    [TestMethod]
    public async Task EntityType_OnImportActions_PatchedToEnum()
    {
        var schema = await TransformAndGetSchema(ScalarParam("entityType"), "api/v1/import/actions");
        CollectionAssert.AreEquivalent(new[] { "Quote", "Source", "Character", "Person", "Conversation", "StageDirection", "SoundCue" },
            schema!.Enum!.Select(v => v!.ToString()).ToArray());
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Array enum params patched on items, not the array schema itself

    [TestMethod]
    public async Task Type_OnQuotes_PatchedOnItemsSchema()
    {
        var schema = await TransformAndGetSchema(ArrayParam("type"), "api/v1/quotes");
        Assert.IsNull(schema!.Enum);
        CollectionAssert.AreEquivalent(new[] { "movie", "tv", "anime", "book", "person" },
            ((OpenApiSchema)schema.Items!).Enum!.Select(v => v!.ToString()).ToArray());
    }

    [TestMethod]
    public async Task Type_OnQuotesWithTrailingSlash_PatchedOnItemsSchema()
    {
        // GetAll is registered as group.MapGet("/", GetAll), which reports RelativePath
        // "api/v1/quotes/" (trailing slash) rather than the bare "api/v1/quotes" the other
        // two /quotes paths use — this silently broke both this transformer and the
        // pre-existing YearParameterSchemaTransformer until caught live.
        var schema = await TransformAndGetSchema(ArrayParam("type"), "api/v1/quotes/");
        CollectionAssert.AreEquivalent(new[] { "movie", "tv", "anime", "book", "person" },
            ((OpenApiSchema)schema!.Items!).Enum!.Select(v => v!.ToString()).ToArray());
    }

    [TestMethod]
    public async Task Type_OnRandom_PatchedOnItemsSchema()
    {
        var schema = await TransformAndGetSchema(ArrayParam("type"), "api/v1/quotes/random");
        CollectionAssert.AreEquivalent(new[] { "movie", "tv", "anime", "book", "person" },
            ((OpenApiSchema)schema!.Items!).Enum!.Select(v => v!.ToString()).ToArray());
    }

    [TestMethod]
    public async Task Type_OnSearch_PatchedOnItemsSchema()
    {
        var schema = await TransformAndGetSchema(ArrayParam("type"), "api/v1/quotes/search");
        CollectionAssert.AreEquivalent(new[] { "movie", "tv", "anime", "book", "person" },
            ((OpenApiSchema)schema!.Items!).Enum!.Select(v => v!.ToString()).ToArray());
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Params NOT patched

    [TestMethod]
    public async Task Genre_OnQuotes_NotPatched()
    {
        var schema = await TransformAndGetSchema(ArrayParam("genre"), "api/v1/quotes");
        Assert.IsNull(((OpenApiSchema)schema!.Items!).Enum);
    }

    [TestMethod]
    public async Task Field_OnUnrelatedPath_NotPatched()
    {
        var schema = await TransformAndGetSchema(ScalarParam("field"), "api/v1/admin/backup");
        Assert.IsNull(schema!.Enum);
    }

    [TestMethod]
    public async Task BatchId_OnImportActions_NotPatched()
    {
        var schema = await TransformAndGetSchema(ScalarParam("batchId"), "api/v1/import/actions");
        Assert.IsNull(schema!.Enum);
    }

    #endregion
}
