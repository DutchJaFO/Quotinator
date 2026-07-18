using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Quotinator.Api.OpenApi;

namespace Quotinator.Api.Tests.OpenApi;

[TestClass]
public class NumericParameterSchemaTransformerTests
{
    // -------------------------------------------------------------------------
    #region Helpers

    private static OpenApiParameter NumericParam(string name) => new()
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

    private static async Task<OpenApiSchema?> TransformAndGetSchema(string paramName, string path)
    {
        var transformer = new NumericParameterSchemaTransformer();
        var operation   = new OpenApiOperation { Parameters = [NumericParam(paramName)] };
        await transformer.TransformAsync(operation, Context(path), CancellationToken.None);
        return (operation.Parameters[0] as OpenApiParameter)?.Schema as OpenApiSchema;
    }

    private static async Task<JsonSchemaType?> TransformAndGetType(string paramName, string path)
        => (await TransformAndGetSchema(paramName, path))?.Type;

    private const JsonSchemaType Integer = JsonSchemaType.Integer | JsonSchemaType.Null;
    private const JsonSchemaType OriginalString = JsonSchemaType.String | JsonSchemaType.Null;

    #endregion

    // -------------------------------------------------------------------------
    #region page/pageSize/n/limit patched on their own paths (#194)

    [TestMethod]
    public async Task Page_OnPaginatedList_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("page", "api/v1/quotes"));

    [TestMethod]
    public async Task PageSize_OnPaginatedList_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("pageSize", "api/v1/quotes"));

    [TestMethod]
    public async Task N_OnRandom_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("n", "api/v1/quotes/random"));

    [TestMethod]
    public async Task Limit_OnSearch_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("limit", "api/v1/quotes/search"));

    [TestMethod]
    public async Task Page_OnPaginatedList_PublishesDefaultOfOne()
        => Assert.AreEqual(1, (await TransformAndGetSchema("page", "api/v1/quotes"))?.Default?.GetValue<int>());

    [TestMethod]
    public async Task PageSize_OnPaginatedList_PublishesDefaultOfTwenty()
        => Assert.AreEqual(20, (await TransformAndGetSchema("pageSize", "api/v1/quotes"))?.Default?.GetValue<int>());

    [TestMethod]
    public async Task N_OnRandom_PublishesDefaultOfOne()
        => Assert.AreEqual(1, (await TransformAndGetSchema("n", "api/v1/quotes/random"))?.Default?.GetValue<int>());

    [TestMethod]
    public async Task Limit_OnSearch_PublishesDefaultOfTwenty()
        => Assert.AreEqual(20, (await TransformAndGetSchema("limit", "api/v1/quotes/search"))?.Default?.GetValue<int>());

    [TestMethod]
    public async Task Page_OnPaginatedListWithTrailingSlash_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("page", "api/v1/quotes/"));

    #endregion

    // -------------------------------------------------------------------------
    #region Year params still patched after the rename (regression)

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

    [TestMethod]
    public async Task YearFrom_DoesNotPublishADefault()
        => Assert.IsNull((await TransformAndGetSchema("yearFrom", "api/v1/quotes"))?.Default);

    #endregion

    // -------------------------------------------------------------------------
    #region Nullability is preserved, not flattened to bare integer

    [TestMethod]
    public async Task OptionalParam_RetainsNullableInteger_NotBareInteger()
        => Assert.AreEqual(JsonSchemaType.Integer | JsonSchemaType.Null,
            await TransformAndGetType("page", "api/v1/quotes"));

    #endregion

    // -------------------------------------------------------------------------
    #region Numeric params NOT patched on other paths

    [TestMethod]
    public async Task YearFrom_OnUnrelatedPath_NotPatched()
        => Assert.AreEqual(OriginalString, await TransformAndGetType("yearFrom", "api/v1/admin/backup"));

    [TestMethod]
    public async Task YearFrom_OnGetById_NotPatched()
        => Assert.AreEqual(OriginalString, await TransformAndGetType("yearFrom", "api/v1/quotes/{id}"));

    [TestMethod]
    public async Task Page_OnUnrelatedPath_NotPatched()
        => Assert.AreEqual(OriginalString, await TransformAndGetType("page", "api/v1/admin/backup"));

    #endregion

    // -------------------------------------------------------------------------
    #region page/pageSize patched on admin/audit and import/actions (#195)

    [TestMethod]
    public async Task Page_OnAdminAudit_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("page", "api/v1/admin/audit"));

    [TestMethod]
    public async Task PageSize_OnAdminAudit_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("pageSize", "api/v1/admin/audit"));

    [TestMethod]
    public async Task Page_OnAdminAudit_PublishesDefaultOfOne()
        => Assert.AreEqual(1, (await TransformAndGetSchema("page", "api/v1/admin/audit"))?.Default?.GetValue<int>());

    [TestMethod]
    public async Task PageSize_OnAdminAudit_PublishesDefaultOfTwenty()
        => Assert.AreEqual(20, (await TransformAndGetSchema("pageSize", "api/v1/admin/audit"))?.Default?.GetValue<int>());

    [TestMethod]
    public async Task Page_OnImportActions_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("page", "api/v1/import/actions"));

    [TestMethod]
    public async Task PageSize_OnImportActions_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("pageSize", "api/v1/import/actions"));

    [TestMethod]
    public async Task Page_OnImportActions_PublishesDefaultOfOne()
        => Assert.AreEqual(1, (await TransformAndGetSchema("page", "api/v1/import/actions"))?.Default?.GetValue<int>());

    [TestMethod]
    public async Task PageSize_OnImportActions_PublishesDefaultOfTwenty()
        => Assert.AreEqual(20, (await TransformAndGetSchema("pageSize", "api/v1/import/actions"))?.Default?.GetValue<int>());

    #endregion

    // -------------------------------------------------------------------------
    #region page/pageSize patched on masterdata/sources (#184)

    [TestMethod]
    public async Task Page_OnMasterDataSources_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("page", "api/v1/masterdata/sources"));

    [TestMethod]
    public async Task PageSize_OnMasterDataSources_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("pageSize", "api/v1/masterdata/sources"));

    [TestMethod]
    public async Task Page_OnMasterDataSources_PublishesDefaultOfOne()
        => Assert.AreEqual(1, (await TransformAndGetSchema("page", "api/v1/masterdata/sources"))?.Default?.GetValue<int>());

    [TestMethod]
    public async Task PageSize_OnMasterDataSources_PublishesDefaultOfTwenty()
        => Assert.AreEqual(20, (await TransformAndGetSchema("pageSize", "api/v1/masterdata/sources"))?.Default?.GetValue<int>());

    #endregion

    // -------------------------------------------------------------------------
    #region page/pageSize patched on masterdata/characters (#185)

    [TestMethod]
    public async Task Page_OnMasterDataCharacters_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("page", "api/v1/masterdata/characters"));

    [TestMethod]
    public async Task PageSize_OnMasterDataCharacters_PatchedToInteger()
        => Assert.AreEqual(Integer, await TransformAndGetType("pageSize", "api/v1/masterdata/characters"));

    [TestMethod]
    public async Task Page_OnMasterDataCharacters_PublishesDefaultOfOne()
        => Assert.AreEqual(1, (await TransformAndGetSchema("page", "api/v1/masterdata/characters"))?.Default?.GetValue<int>());

    [TestMethod]
    public async Task PageSize_OnMasterDataCharacters_PublishesDefaultOfTwenty()
        => Assert.AreEqual(20, (await TransformAndGetSchema("pageSize", "api/v1/masterdata/characters"))?.Default?.GetValue<int>());

    #endregion

    // -------------------------------------------------------------------------
    #region Non-numeric params NOT patched even on target paths

    [TestMethod]
    public async Task TypeParam_OnRandom_NotPatched()
        => Assert.AreEqual(OriginalString, await TransformAndGetType("type", "api/v1/quotes/random"));

    [TestMethod]
    public async Task GenreParam_OnRandom_NotPatched()
        => Assert.AreEqual(OriginalString, await TransformAndGetType("genre", "api/v1/quotes/random"));

    #endregion
}
