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
    public void Parse_EqualsField_ReadsBothPaths()
    {
        IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--equals-field", "pageSize=totalCount"], out _);

        Assert.AreEqual(new JsonExpectation(JsonExpectationKind.EqualToField, "pageSize", "totalCount"), parsed[0]);
    }

    [TestMethod]
    public void Parse_LengthEqualsField_ReadsBothPaths()
    {
        IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--length-equals-field", "items=totalCount"], out _);

        Assert.AreEqual(new JsonExpectation(JsonExpectationKind.LengthEqualToField, "items", "totalCount"), parsed[0]);
    }

    [TestMethod]
    public void Parse_LengthEquals_ReadsPathAndLength()
    {
        IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--length-equals", "items=20"], out _);

        Assert.AreEqual(new JsonExpectation(JsonExpectationKind.LengthEquals, "items", "20"), parsed[0]);
    }

    [TestMethod]
    public void Parse_Present_ReadsPathWithoutValue()
    {
        IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--present", "reason"], out _);

        Assert.AreEqual(new JsonExpectation(JsonExpectationKind.Present, "reason", null), parsed[0]);
    }

    [TestMethod]
    public void Parse_Absent_ReadsPathWithoutValue()
    {
        IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--absent", "items.0.resolution"], out _);

        Assert.AreEqual(new JsonExpectation(JsonExpectationKind.Absent, "items.0.resolution", null), parsed[0]);
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
    public void Parse_EqualsJson_ExpectsTheValueFromTheOtherFile()
    {
        string file = WriteTempFile("[{\"Recorded\": 5}]");

        try
        {
            IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--equals-json", $"0.Recorded={file}#0.Recorded"], out _);

            Assert.AreEqual(new JsonExpectation(JsonExpectationKind.EqualTo, "0.Recorded", "5"), parsed[0]);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void Parse_GreaterThanJson_ExpectsMoreThanTheValueFromTheOtherFile()
    {
        string file = WriteTempFile("{\"database\": {\"schemaVersion\": 5}}");

        try
        {
            IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--greater-than-json", $"database.schemaVersion={file}#database.schemaVersion"], out _);

            Assert.AreEqual(new JsonExpectation(JsonExpectationKind.GreaterThan, "database.schemaVersion", "5"), parsed[0]);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void Parse_LengthEqualsJson_ExpectsALengthOfTheValueFromTheOtherFile()
    {
        string file = WriteTempFile("[{\"DataFiles\": 5}]");

        try
        {
            IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--length-equals-json", $"files={file}#0.DataFiles"], out _);

            Assert.AreEqual(new JsonExpectation(JsonExpectationKind.LengthEquals, "files", "5"), parsed[0]);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void Parse_EqualsJsonWithAPathTheFileDoesNotHave_IsAnError()
    {
        string file = WriteTempFile("{}");

        try
        {
            _ = JsonExpectationParser.Parse(["--equals-json", $"a={file}#missing"], out IReadOnlyList<string> errors);

            Assert.HasCount(1, errors);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void Parse_EqualsJsonWithAMissingFile_IsAnError()
    {
        string file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

        _ = JsonExpectationParser.Parse(["--equals-json", $"a={file}#a"], out IReadOnlyList<string> errors);

        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public void Parse_EqualsXml_ExpectsTheElementsTextFromTheFile()
    {
        string file = WriteTempFile("<Project><PropertyGroup><Version>1.2.3-alpha</Version></PropertyGroup></Project>");

        try
        {
            IReadOnlyList<JsonExpectation> parsed = JsonExpectationParser.Parse(["--equals-xml", $"version={file}#Version"], out _);

            Assert.AreEqual(new JsonExpectation(JsonExpectationKind.EqualTo, "version", "1.2.3-alpha"), parsed[0]);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void Parse_EqualsXmlWithAnElementTheFileDoesNotHave_IsAnError()
    {
        string file = WriteTempFile("<Project />");

        try
        {
            _ = JsonExpectationParser.Parse(["--equals-xml", $"version={file}#Version"], out IReadOnlyList<string> errors);

            Assert.HasCount(1, errors);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void Parse_EqualsXmlWithAMissingFile_IsAnError()
    {
        string file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");

        _ = JsonExpectationParser.Parse(["--equals-xml", $"version={file}#Version"], out IReadOnlyList<string> errors);

        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public void Parse_EqualsXmlWithoutAnElementName_IsAnError()
    {
        _ = JsonExpectationParser.Parse(["--equals-xml", "version=Directory.Build.props"], out IReadOnlyList<string> errors);

        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public void Parse_WellFormedOptions_HaveNoErrors()
    {
        _ = JsonExpectationParser.Parse(["--equals", "status=x", "--present", "reason"], out IReadOnlyList<string> errors);

        Assert.IsEmpty(errors);
    }

    private static string WriteTempFile(string content)
    {
        string file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        File.WriteAllText(file, content);

        return file;
    }
}
