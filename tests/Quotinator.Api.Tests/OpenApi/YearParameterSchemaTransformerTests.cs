using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Quotinator.Api.OpenApi;

namespace Quotinator.Api.Tests.OpenApi;

[TestClass]
public class YearParameterSchemaTransformerTests
{
    // -------------------------------------------------------------------------
    #region Helpers

    private static OpenApiParameter YearParam(string name) => new()
    {
        Name   = name,
        Schema = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null }
    };

    private static OpenApiOperationTransformerContext Context(string relativePath) =>
        new()
        {
            DocumentName         = "v1",
            Description          = new ApiDescription { RelativePath = relativePath },
            ApplicationServices  = new ServiceCollection().BuildServiceProvider()
        };

    private static async Task<JsonSchemaType?> TransformAndGetType(string paramName, string path)
    {
        var transformer = new YearParameterSchemaTransformer();
        var operation   = new OpenApiOperation { Parameters = [YearParam(paramName)] };
        await transformer.TransformAsync(operation, Context(path), CancellationToken.None);
        return (operation.Parameters[0] as OpenApiParameter)?.Schema?.Type;
    }

    private const JsonSchemaType Integer = JsonSchemaType.Integer | JsonSchemaType.Null;
    private const JsonSchemaType OriginalString = JsonSchemaType.String | JsonSchemaType.Null;

    #endregion

    // -------------------------------------------------------------------------
    #region Year params patched on the three target paths

    [TestMethod]
    public async Task YearFrom_OnRandom_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("yearFrom", "api/v1/quotes/random"));

    [TestMethod]
    public async Task YearTo_OnRandom_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("yearTo", "api/v1/quotes/random"));

    [TestMethod]
    public async Task Year_OnRandom_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("year", "api/v1/quotes/random"));

    [TestMethod]
    public async Task Decade_OnRandom_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("decade", "api/v1/quotes/random"));

    [TestMethod]
    public async Task YearFrom_OnSearch_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("yearFrom", "api/v1/quotes/search"));

    [TestMethod]
    public async Task YearFrom_OnPaginatedList_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("yearFrom", "api/v1/quotes"));

    [TestMethod]
    public async Task YearFrom_OnPaginatedListWithTrailingSlash_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("yearFrom", "api/v1/quotes/"));

    #endregion

    // -------------------------------------------------------------------------
    #region Year params NOT patched on other paths

    [TestMethod]
    public async Task YearFrom_OnUnrelatedPath_NotPatched()
        => Assert.AreEqual(OriginalString, await TransformAndGetType("yearFrom", "api/v1/admin/backup"));

    [TestMethod]
    public async Task YearFrom_OnGetById_NotPatched()
        => Assert.AreEqual(OriginalString, await TransformAndGetType("yearFrom", "api/v1/quotes/{id}"));

    #endregion

    // -------------------------------------------------------------------------
    #region Non-year params NOT patched even on target paths

    [TestMethod]
    public async Task TypeParam_OnRandom_NotPatched()
        => Assert.AreEqual(OriginalString, await TransformAndGetType("type", "api/v1/quotes/random"));

    [TestMethod]
    public async Task GenreParam_OnRandom_NotPatched()
        => Assert.AreEqual(OriginalString, await TransformAndGetType("genre", "api/v1/quotes/random"));

    #endregion
}
