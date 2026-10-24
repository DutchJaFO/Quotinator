using Quotinator.Tools.TestProbe.Enums;
using Quotinator.Tools.TestProbe.Json;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Tests.Json;

[TestClass]
public sealed class JsonExpectationEvaluatorTests
{
    private const string Health = """{"status":"unhealthy","reason":"locked","empty":"","list":["one","two"],"none":[],"nothing":null,"count":3}""";

    [TestMethod]
    public void EqualTo_MatchingValue_Holds()
        => Assert.IsTrue(Evaluate(JsonExpectationKind.EqualTo, "status", "unhealthy").Holds);

    [TestMethod]
    public void EqualTo_DifferentValue_Fails()
        => Assert.IsFalse(Evaluate(JsonExpectationKind.EqualTo, "status", "healthy").Holds);

    [TestMethod]
    public void EqualTo_DifferentCasing_Fails()
        => Assert.IsFalse(Evaluate(JsonExpectationKind.EqualTo, "status", "Unhealthy").Holds);

    [TestMethod]
    public void EqualTo_Number_ComparesItsJsonText()
        => Assert.IsTrue(Evaluate(JsonExpectationKind.EqualTo, "count", "3").Holds);

    [TestMethod]
    public void EqualTo_MissingField_Fails()
        => Assert.IsFalse(Evaluate(JsonExpectationKind.EqualTo, "absent", "x").Holds);

    [TestMethod]
    public void Contains_StringContainingText_Holds()
        => Assert.IsTrue(Evaluate(JsonExpectationKind.Contains, "reason", "lock").Holds);

    [TestMethod]
    public void Contains_StringWithoutText_Fails()
        => Assert.IsFalse(Evaluate(JsonExpectationKind.Contains, "reason", "corrupt").Holds);

    [TestMethod]
    public void Contains_ArrayWithAnElementContainingText_Holds()
        => Assert.IsTrue(Evaluate(JsonExpectationKind.Contains, "list", "tw").Holds);

    [TestMethod]
    public void Contains_ArrayWithoutSuchElement_Fails()
        => Assert.IsFalse(Evaluate(JsonExpectationKind.Contains, "list", "three").Holds);

    [TestMethod]
    public void Present_ExistingField_Holds()
        => Assert.IsTrue(Evaluate(JsonExpectationKind.Present, "nothing", null).Holds);

    [TestMethod]
    public void Present_MissingField_Fails()
        => Assert.IsFalse(Evaluate(JsonExpectationKind.Present, "absent", null).Holds);

    [TestMethod]
    public void NotEmpty_NonEmptyString_Holds()
        => Assert.IsTrue(Evaluate(JsonExpectationKind.NotEmpty, "reason", null).Holds);

    [TestMethod]
    public void NotEmpty_EmptyString_Fails()
        => Assert.IsFalse(Evaluate(JsonExpectationKind.NotEmpty, "empty", null).Holds);

    [TestMethod]
    public void NotEmpty_WhitespaceString_Fails()
    {
        ExpectationResult result = JsonExpectationEvaluator.Evaluate(
            """{"reason":"  "}""", [new JsonExpectation(JsonExpectationKind.NotEmpty, "reason", null)]);

        Assert.IsFalse(result.Holds);
    }

    [TestMethod]
    public void NotEmpty_EmptyArray_Fails()
        => Assert.IsFalse(Evaluate(JsonExpectationKind.NotEmpty, "none", null).Holds);

    [TestMethod]
    public void NotEmpty_Null_Fails()
        => Assert.IsFalse(Evaluate(JsonExpectationKind.NotEmpty, "nothing", null).Holds);

    [TestMethod]
    public void NotEmpty_MissingField_Fails()
        => Assert.IsFalse(Evaluate(JsonExpectationKind.NotEmpty, "absent", null).Holds);

    [TestMethod]
    public void GreaterThan_LargerNumber_Holds()
        => Assert.IsTrue(Evaluate(JsonExpectationKind.GreaterThan, "count", "0").Holds);

    [TestMethod]
    public void GreaterThan_EqualNumber_Fails()
        => Assert.IsFalse(Evaluate(JsonExpectationKind.GreaterThan, "count", "3").Holds);

    [TestMethod]
    public void GreaterThan_String_Fails()
        => Assert.IsFalse(Evaluate(JsonExpectationKind.GreaterThan, "status", "0").Holds);

    [TestMethod]
    public void Evaluate_OneOfSeveralFails_ReportsOnlyThatFailure()
    {
        ExpectationResult result = JsonExpectationEvaluator.Evaluate(Health,
        [
            new JsonExpectation(JsonExpectationKind.EqualTo, "status", "unhealthy"),
            new JsonExpectation(JsonExpectationKind.Present, "absent", null),
        ]);

        Assert.HasCount(1, result.Failures);
    }

    [TestMethod]
    public void Evaluate_Failure_NamesThePath()
        => Assert.Contains("status", Evaluate(JsonExpectationKind.EqualTo, "status", "healthy").Failures[0]);

    [TestMethod]
    public void Evaluate_ObservesTheFieldValue()
        => Assert.Contains(line => line.Contains("locked"), Evaluate(JsonExpectationKind.Present, "reason", null).Observations);

    [TestMethod]
    public void Evaluate_TextThatIsNotJson_Fails()
    {
        ExpectationResult result = JsonExpectationEvaluator.Evaluate(
            "<html>", [new JsonExpectation(JsonExpectationKind.Present, "status", null)]);

        Assert.IsFalse(result.Holds);
    }

    [TestMethod]
    public void Evaluate_NoExpectations_Fails()
        => Assert.IsFalse(JsonExpectationEvaluator.Evaluate(Health, []).Holds);

    private static ExpectationResult Evaluate(JsonExpectationKind kind, string path, string? value)
        => JsonExpectationEvaluator.Evaluate(Health, [new JsonExpectation(kind, path, value)]);
}
