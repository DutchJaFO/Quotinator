using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class ImportRequestSettingsParserTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void TryParse_MissingOrBlank_SucceedsWithNullSettings(string? json)
    {
        var success = ImportRequestSettingsParser.TryParse(json, out var settings);

        Assert.IsTrue(success);
        Assert.IsNull(settings);
    }

    [TestMethod]
    public void TryParse_ValidJson_ParsesAllFields()
    {
        var success = ImportRequestSettingsParser.TryParse(
            """{"converter":"csv","duplicateResolution":{"default":"merge-theirs"},"enrich":true}""",
            out var settings);

        Assert.IsTrue(success);
        Assert.AreEqual("csv", settings?.Converter);
        Assert.AreEqual(DuplicateResolutionPolicy.MergeTheirs, settings?.DuplicateResolution?.Default);
        Assert.IsTrue(settings?.Enrich);
    }

    [TestMethod]
    public void TryParse_ValidJsonOmittingAllFields_ParsesToEmptySettings()
    {
        var success = ImportRequestSettingsParser.TryParse("{}", out var settings);

        Assert.IsTrue(success);
        Assert.IsNotNull(settings);
        Assert.IsNull(settings!.Converter);
        Assert.IsNull(settings.DuplicateResolution);
        Assert.IsFalse(settings.Enrich);
    }

    [TestMethod]
    [DataRow("{ not json")]
    [DataRow("""{"duplicateResolution":{"default":"not-a-real-policy"}}""")]
    public void TryParse_MalformedOrInvalidPolicyValue_ReturnsFalse(string json)
    {
        var success = ImportRequestSettingsParser.TryParse(json, out var settings);

        Assert.IsFalse(success);
        Assert.IsNull(settings);
    }
}
