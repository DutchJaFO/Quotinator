using System.Text.Json;
using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class DuplicateResolutionPolicyJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new DuplicateResolutionPolicyJsonConverter() }
    };

    [TestMethod]
    [DataRow(DuplicateResolutionPolicy.Skip,        "\"skip\"")]
    [DataRow(DuplicateResolutionPolicy.NewestWins,  "\"newest-wins\"")]
    [DataRow(DuplicateResolutionPolicy.MergeOurs,   "\"merge-ours\"")]
    [DataRow(DuplicateResolutionPolicy.MergeTheirs, "\"merge-theirs\"")]
    [DataRow(DuplicateResolutionPolicy.Review,      "\"review\"")]
    public void Serialize_AllFiveValues_ProducesKebabCaseWireString(DuplicateResolutionPolicy policy, string expectedJson)
    {
        var json = JsonSerializer.Serialize(policy, Options);

        Assert.AreEqual(expectedJson, json);
    }

    [TestMethod]
    [DataRow("\"skip\"",         DuplicateResolutionPolicy.Skip)]
    [DataRow("\"newest-wins\"",  DuplicateResolutionPolicy.NewestWins)]
    [DataRow("\"merge-ours\"",   DuplicateResolutionPolicy.MergeOurs)]
    [DataRow("\"merge-theirs\"", DuplicateResolutionPolicy.MergeTheirs)]
    [DataRow("\"review\"",       DuplicateResolutionPolicy.Review)]
    public void Deserialize_AllFiveWireStrings_ProducesCorrectEnumValue(string json, DuplicateResolutionPolicy expected)
    {
        var policy = JsonSerializer.Deserialize<DuplicateResolutionPolicy>(json, Options);

        Assert.AreEqual(expected, policy);
    }

    [TestMethod]
    public void ManifestPolicyDto_DefaultKeyOmitted_ResolvesToNewestWins()
    {
        const string json = """{"quotes":"skip"}""";

        var dto = JsonSerializer.Deserialize<ManifestPolicyDto>(json)!;

        Assert.AreEqual(DuplicateResolutionPolicy.NewestWins, dto.Default,
            "duplicateResolution section present but 'default' key omitted must still resolve to NewestWins");
        Assert.AreEqual(DuplicateResolutionPolicy.Skip, dto.Quotes);
    }

    [TestMethod]
    public void ManifestPolicyDto_MultiWordValues_RoundTripViaKebabCase()
    {
        const string json = """{"default":"merge-ours","quotes":"merge-theirs"}""";

        var dto = JsonSerializer.Deserialize<ManifestPolicyDto>(json)!;

        Assert.AreEqual(DuplicateResolutionPolicy.MergeOurs, dto.Default);
        Assert.AreEqual(DuplicateResolutionPolicy.MergeTheirs, dto.Quotes);
    }
}
