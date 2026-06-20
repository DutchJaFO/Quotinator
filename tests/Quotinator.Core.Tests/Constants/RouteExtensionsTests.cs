using Quotinator.Constants.Routes;

namespace Quotinator.Core.Tests.Constants;

[TestClass]
public class RouteExtensionsTests
{
    [TestMethod]
    public void AsLink_LeadingSlash_Stripped()
    {
        Assert.AreEqual("scalar/v1", "/scalar/v1".AsLink());
    }

    [TestMethod]
    public void AsLink_NoLeadingSlash_Unchanged()
    {
        Assert.AreEqual("scalar/v1", "scalar/v1".AsLink());
    }

    [TestMethod]
    public void AsLink_MultipleLeadingSlashes_AllStripped()
    {
        Assert.AreEqual("api/v1/health", "//api/v1/health".AsLink());
    }

    [TestMethod]
    public void AsLink_AllApiRoutes_ProduceRelativePaths()
    {
        // Ensures no ApiRoutes constant accidentally lacks a leading slash,
        // which would cause AsLink() to return the same string (fine) but also
        // catches any constant that starts with something other than '/'.
        Assert.IsFalse(ApiRoutes.Health.AsLink().StartsWith('/'));
        Assert.IsFalse(ApiRoutes.Version.AsLink().StartsWith('/'));
        Assert.IsFalse(ApiRoutes.CultureSet.AsLink().StartsWith('/'));
        Assert.IsFalse(ApiRoutes.ScalarUi.AsLink().StartsWith('/'));
        Assert.IsFalse(ApiRoutes.OpenApiSpec.AsLink().StartsWith('/'));
    }
}
