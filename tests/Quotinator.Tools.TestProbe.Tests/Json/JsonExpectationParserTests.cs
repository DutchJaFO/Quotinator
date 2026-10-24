using Quotinator.Tools.TestProbe.Enums;
using Quotinator.Tools.TestProbe.Json;

namespace Quotinator.Tools.TestProbe.Tests.Json;

[TestClass]
public sealed class JsonExpectationParserTests
{
    [TestMethod]
    public void Parse_Equals_SplitsPathAndValueAtTheFirstEqualsSign()
    {
        IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--equals", "detail=a=b"], out _);

        Assert.AreEqual(new JsonExpectation(JsonExpectationKind.EqualTo, "detail", "a=b"), parsed[0]);
    }

    [TestMethod]
    public void Parse_Contains_ReadsPathAndText()
    {
        IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--contains", "reason=locked"], out _);

        Assert.AreEqual(new JsonExpectation(JsonExpectationKind.Contains, "reason", "locked"), parsed[0]);
    }

    [TestMethod]
    public void Parse_Present_ReadsPathWithoutValue()
    {
        IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--present", "reason"], out _);

        Assert.AreEqual(new JsonExpectation(JsonExpectationKind.Present, "reason", null), parsed[0]);
    }

    [TestMethod]
    public void Parse_NotEmpty_ReadsPathWithoutValue()
    {
        IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--not-empty", "reason"], out _);

        Assert.AreEqual(new JsonExpectation(JsonExpectationKind.NotEmpty, "reason", null), parsed[0]);
    }

    [TestMethod]
    public void Parse_GreaterThan_ReadsPathAndNumber()
    {
        IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--greater-than", "database.quotes=0"], out _);

        Assert.AreEqual(new JsonExpectation(JsonExpectationKind.GreaterThan, "database.quotes", "0"), parsed[0]);
    }

    [TestMethod]
    public void Parse_GreaterThanWithoutANumber_IsAnError()
    {
        _ = JsonExpectationParser.Parse(["--greater-than", "database.quotes=many"], out IReadOnlyList<string> errors);

        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public void Parse_SeveralOptions_ReturnsOneExpectationEach()
    {
        IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(
            ["--equals", "status=unhealthy", "--not-empty", "reason", "--present", "status"], out _);

        Assert.HasCount(3, parsed);
    }

    [TestMethod]
    public void Parse_EqualsWithoutAnEqualsSign_IsAnError()
    {
        _ = JsonExpectationParser.Parse(["--equals", "status"], out IReadOnlyList<string> errors);

        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public void Parse_OptionWithoutItsValue_IsAnError()
    {
        _ = JsonExpectationParser.Parse(["--present"], out IReadOnlyList<string> errors);

        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public void Parse_UnknownOption_IsAnError()
    {
        _ = JsonExpectationParser.Parse(["--equal", "status=x"], out IReadOnlyList<string> errors);

        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public void Parse_WellFormedOptions_HaveNoErrors()
    {
        _ = JsonExpectationParser.Parse(["--equals", "status=x", "--present", "reason"], out IReadOnlyList<string> errors);

        Assert.IsEmpty(errors);
    }
}
