using Microsoft.VisualStudio.TestTools.UnitTesting;
using Quotinator.Api.Startup;

namespace Quotinator.Api.Tests.Startup;

[TestClass]
public class StartupSummaryLoggerTests
{
    // -------------------------------------------------------------------------
    #region ResolveUrls — HA ingress

    [TestMethod]
    public void ResolveUrls_HaMode_AllFieldsReturnHaMessage()
    {
        var (restApi, ui, spec) = StartupSummaryLogger.ResolveUrls(
            ["http://0.0.0.0:8080"], isHa: true, sslEnabled: false, localIp: "192.168.1.1");

        const string expected = "(HA ingress - URL determined at runtime)";
        Assert.AreEqual(expected, restApi);
        Assert.AreEqual(expected, ui);
        Assert.AreEqual(expected, spec);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region ResolveUrls — no addresses

    [TestMethod]
    public void ResolveUrls_NoAddresses_AllFieldsReturnNotAvailable()
    {
        var (restApi, ui, spec) = StartupSummaryLogger.ResolveUrls(
            [], isHa: false, sslEnabled: false, localIp: "192.168.1.1");

        const string expected = "(address not available)";
        Assert.AreEqual(expected, restApi);
        Assert.AreEqual(expected, ui);
        Assert.AreEqual(expected, spec);
    }

    [TestMethod]
    public void ResolveUrls_OnlyIngressPort8099_AllFieldsReturnNotAvailable()
    {
        var (restApi, ui, spec) = StartupSummaryLogger.ResolveUrls(
            ["http://0.0.0.0:8099"], isHa: false, sslEnabled: false, localIp: "192.168.1.1");

        const string expected = "(address not available)";
        Assert.AreEqual(expected, restApi);
        Assert.AreEqual(expected, ui);
        Assert.AreEqual(expected, spec);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region ResolveUrls — URL formatting

    [TestMethod]
    public void ResolveUrls_HttpWildcard_ReplacesWithLocalIp()
    {
        var (restApi, ui, spec) = StartupSummaryLogger.ResolveUrls(
            ["http://0.0.0.0:8080"], isHa: false, sslEnabled: false, localIp: "192.168.1.5");

        Assert.AreEqual("http://192.168.1.5:8080/api/v1/", restApi);
        Assert.AreEqual("http://192.168.1.5:8080/scalar/v1", ui);
        Assert.AreEqual("http://192.168.1.5:8080/openapi/v1.json", spec);
    }

    [TestMethod]
    public void ResolveUrls_IPv6Wildcard_ReplacesWithLocalIp()
    {
        var (restApi, ui, spec) = StartupSummaryLogger.ResolveUrls(
            ["http://[::]:8080"], isHa: false, sslEnabled: false, localIp: "192.168.1.5");

        Assert.AreEqual("http://192.168.1.5:8080/api/v1/", restApi);
        Assert.AreEqual("http://192.168.1.5:8080/scalar/v1", ui);
        Assert.AreEqual("http://192.168.1.5:8080/openapi/v1.json", spec);
    }

    [TestMethod]
    public void ResolveUrls_SslEnabled_UsesHttpsScheme()
    {
        var (restApi, ui, spec) = StartupSummaryLogger.ResolveUrls(
            ["http://0.0.0.0:8080"], isHa: false, sslEnabled: true, localIp: "192.168.1.5");

        Assert.AreEqual("https://192.168.1.5:8080/api/v1/", restApi);
        Assert.AreEqual("https://192.168.1.5:8080/scalar/v1", ui);
        Assert.AreEqual("https://192.168.1.5:8080/openapi/v1.json", spec);
    }

    [TestMethod]
    public void ResolveUrls_MultipleAddresses_UsesPrimarySkippingIngressPort()
    {
        var (restApi, _, _) = StartupSummaryLogger.ResolveUrls(
            ["http://0.0.0.0:8099", "http://0.0.0.0:8080"],
            isHa: false, sslEnabled: false, localIp: "192.168.1.5");

        // 8080 is the primary; 8099 is the HA ingress port and must be skipped
        Assert.AreEqual("http://192.168.1.5:8080/api/v1/", restApi);
    }

    [TestMethod]
    public void ResolveUrls_LocalhostAddress_PassedThrough()
    {
        var (restApi, _, _) = StartupSummaryLogger.ResolveUrls(
            ["http://localhost:5000"], isHa: false, sslEnabled: false, localIp: "192.168.1.5");

        // Non-wildcard address is not replaced — it passes through as-is
        Assert.AreEqual("http://localhost:5000/api/v1/", restApi);
    }

    #endregion
}
