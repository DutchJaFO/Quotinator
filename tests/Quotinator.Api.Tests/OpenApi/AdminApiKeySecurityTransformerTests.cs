using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Quotinator.Api.Endpoints.Filters;
using Quotinator.Api.OpenApi;

namespace Quotinator.Api.Tests.OpenApi;

[TestClass]
public class AdminApiKeySecurityTransformerTests
{
    // -------------------------------------------------------------------------
    #region Helpers

    private static OpenApiOperationTransformerContext Context(IList<object> endpointMetadata) =>
        new()
        {
            DocumentName = "v1",
            Description = new ApiDescription
            {
                ActionDescriptor = new ActionDescriptor { EndpointMetadata = endpointMetadata }
            },
            ApplicationServices = new ServiceCollection().BuildServiceProvider()
        };

    private static async Task<IList<OpenApiSecurityRequirement>?> TransformAndGetSecurity(IList<object> endpointMetadata)
    {
        var transformer = new AdminApiKeySecurityTransformer();
        var operation = new OpenApiOperation();
        await transformer.TransformAsync(operation, Context(endpointMetadata), CancellationToken.None);
        return operation.Security;
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Marker present

    [TestMethod]
    public async Task MarkerPresent_SetsApiKeySecurityRequirement()
    {
        var security = await TransformAndGetSecurity([AdminApiKeyRequiredMarker.Instance]);

        Assert.IsNotNull(security);
        Assert.AreEqual(1, security!.Count);
        Assert.AreEqual(1, security[0].Count);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Marker absent

    [TestMethod]
    public async Task MarkerAbsent_LeavesSecurityNull()
        => Assert.IsNull(await TransformAndGetSecurity([]));

    [TestMethod]
    public async Task OtherMetadataPresent_WithoutMarker_LeavesSecurityNull()
        => Assert.IsNull(await TransformAndGetSecurity([new object()]));

    #endregion
}
