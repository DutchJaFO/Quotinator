using Quotinator.Tools.TestProbe.Json;

namespace Quotinator.Tools.TestProbe.Tests.Json;

[TestClass]
public sealed class JsonArrayFilterTests
{
    private const string Notifications = """
        {"items": [
          {"kind": "quota", "isDismissed": false, "actions": ["backup"]},
          {"kind": "quota", "isDismissed": true, "actions": []},
          {"kind": "other", "isDismissed": false, "actions": []}
        ], "totalCount": 3}
        """;

    private static readonly string[] OtherOptions = ["--equals", "a=b", "--present", "c"];

    [TestMethod]
    public void Parse_FieldAndValue_ReadsArrayFieldAndValue()
    {
        IReadOnlyList<JsonArrayCondition> parsed = JsonArrayFilter.Parse(["--where", "items:kind=quota"], out _, out _);

        Assert.AreEqual(new JsonArrayCondition("items", "kind", "quota"), parsed[0]);
    }

    [TestMethod]
    public void Parse_FieldWithoutValue_ReadsAPresenceCondition()
    {
        IReadOnlyList<JsonArrayCondition> parsed = JsonArrayFilter.Parse(["--where", "items:actions.0"], out _, out _);

        Assert.AreEqual(new JsonArrayCondition("items", "actions.0", null), parsed[0]);
    }

    [TestMethod]
    public void Parse_LeavesEveryOtherOptionInPlace()
    {
        _ = JsonArrayFilter.Parse(["--equals", "a=b", "--where", "items:kind=quota", "--present", "c"], out IReadOnlyList<string> remaining, out _);

        Assert.AreSequenceEqual(OtherOptions, remaining);
    }

    [TestMethod]
    public void Parse_WithoutAnArrayPath_IsAnError()
    {
        _ = JsonArrayFilter.Parse(["--where", "kind=quota"], out _, out IReadOnlyList<string> errors);

        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public void Parse_WithoutItsValue_IsAnError()
    {
        _ = JsonArrayFilter.Parse(["--where"], out _, out IReadOnlyList<string> errors);

        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public void Apply_KeepsOnlyTheElementsWhoseFieldHoldsTheValue()
    {
        string? filtered = JsonArrayFilter.Apply(Notifications, [new JsonArrayCondition("items", "kind", "quota")], out _);

        Assert.AreEqual(2, Count(filtered));
    }

    [TestMethod]
    public void Apply_SeveralConditions_KeepsOnlyTheElementsMatchingAll()
    {
        string? filtered = JsonArrayFilter.Apply(
            Notifications,
            [new JsonArrayCondition("items", "kind", "quota"), new JsonArrayCondition("items", "isDismissed", "false")],
            out _);

        Assert.AreEqual(1, Count(filtered));
    }

    [TestMethod]
    public void Apply_PresenceCondition_KeepsOnlyTheElementsWhereTheFieldExists()
    {
        string? filtered = JsonArrayFilter.Apply(Notifications, [new JsonArrayCondition("items", "actions.0", null)], out _);

        Assert.AreEqual(1, Count(filtered));
    }

    [TestMethod]
    public void Apply_NoElementMatching_LeavesAnEmptyArray()
    {
        string? filtered = JsonArrayFilter.Apply(Notifications, [new JsonArrayCondition("items", "kind", "Quota")], out _);

        Assert.AreEqual(0, Count(filtered));
    }

    [TestMethod]
    public void Apply_LeavesTheRestOfTheDocumentAsItWas()
    {
        string? filtered = JsonArrayFilter.Apply(Notifications, [new JsonArrayCondition("items", "kind", "other")], out _);

        Assert.Contains("\"totalCount\":3", filtered!);
    }

    [TestMethod]
    public void Apply_NoArrayAtThePath_IsAProblem()
    {
        string? filtered = JsonArrayFilter.Apply(Notifications, [new JsonArrayCondition("totalCount", "kind", "quota")], out string? problem);

        Assert.IsNull(filtered);
        Assert.IsNotNull(problem);
    }

    [TestMethod]
    public void Apply_TextThatIsNotJson_IsAProblem()
    {
        string? filtered = JsonArrayFilter.Apply("<html>", [new JsonArrayCondition("items", "kind", "quota")], out string? problem);

        Assert.IsNull(filtered);
        Assert.IsNotNull(problem);
    }

    private static int Count(string? json)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json!);

        return document.RootElement.GetProperty("items").GetArrayLength();
    }
}
