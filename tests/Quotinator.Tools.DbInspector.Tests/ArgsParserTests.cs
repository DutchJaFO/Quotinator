using Quotinator.Tools.DbInspector;

namespace Quotinator.Tools.DbInspector.Tests;

[TestClass]
public class ArgsParserTests
{
    [TestMethod]
    public void Parse_BothArgsPresent_ReturnsParsedValues()
    {
        QueryArgs? result = ArgsParser.Parse(["--db", "C:/data.db", "--sql", "SELECT 1"]);

        Assert.IsNotNull(result);
        Assert.AreEqual("C:/data.db", result.Value.DbPath);
        Assert.AreEqual("SELECT 1", result.Value.Sql);
    }

    [TestMethod]
    public void Parse_ArgsInReverseOrder_ReturnsParsedValues()
    {
        QueryArgs? result = ArgsParser.Parse(["--sql", "SELECT 1", "--db", "C:/data.db"]);

        Assert.IsNotNull(result);
        Assert.AreEqual("C:/data.db", result.Value.DbPath);
        Assert.AreEqual("SELECT 1", result.Value.Sql);
    }

    [TestMethod]
    public void Parse_JsonOut_IsParsed()
    {
        QueryArgs? result = ArgsParser.Parse(["--db", "C:/data.db", "--sql", "SELECT 1", "--json-out", "C:/out.json"]);

        Assert.IsNotNull(result);
        Assert.AreEqual("C:/out.json", result.Value.JsonOut);
    }

    [TestMethod]
    public void Parse_NoJsonOut_LeavesItNull()
    {
        QueryArgs? result = ArgsParser.Parse(["--db", "C:/data.db", "--sql", "SELECT 1"]);

        Assert.IsNotNull(result);
        Assert.IsNull(result.Value.JsonOut);
    }

    [TestMethod]
    public void Parse_MissingDb_ReturnsNull()
    {
        QueryArgs? result = ArgsParser.Parse(["--sql", "SELECT 1"]);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_MissingSql_ReturnsNull()
    {
        QueryArgs? result = ArgsParser.Parse(["--db", "C:/data.db"]);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_NoArgs_ReturnsNull()
    {
        QueryArgs? result = ArgsParser.Parse([]);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_UnknownFlag_IsIgnored()
    {
        QueryArgs? result = ArgsParser.Parse(["--verbose", "--db", "C:/data.db", "--sql", "SELECT 1"]);

        Assert.IsNotNull(result);
        Assert.AreEqual("C:/data.db", result.Value.DbPath);
    }
}
