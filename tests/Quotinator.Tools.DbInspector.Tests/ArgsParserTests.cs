using Quotinator.Tools.DbInspector;

namespace Quotinator.Tools.DbInspector.Tests;

[TestClass]
public class ArgsParserTests
{
    [TestMethod]
    public void Parse_BothArgsPresent_ReturnsParsedValues()
    {
        var result = ArgsParser.Parse(["--db", "C:/data.db", "--sql", "SELECT 1"]);

        Assert.IsNotNull(result);
        Assert.AreEqual("C:/data.db", result.Value.DbPath);
        Assert.AreEqual("SELECT 1", result.Value.Sql);
    }

    [TestMethod]
    public void Parse_ArgsInReverseOrder_ReturnsParsedValues()
    {
        var result = ArgsParser.Parse(["--sql", "SELECT 1", "--db", "C:/data.db"]);

        Assert.IsNotNull(result);
        Assert.AreEqual("C:/data.db", result.Value.DbPath);
        Assert.AreEqual("SELECT 1", result.Value.Sql);
    }

    [TestMethod]
    public void Parse_MissingDb_ReturnsNull()
    {
        var result = ArgsParser.Parse(["--sql", "SELECT 1"]);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_MissingSql_ReturnsNull()
    {
        var result = ArgsParser.Parse(["--db", "C:/data.db"]);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_NoArgs_ReturnsNull()
    {
        var result = ArgsParser.Parse([]);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_UnknownFlag_IsIgnored()
    {
        var result = ArgsParser.Parse(["--verbose", "--db", "C:/data.db", "--sql", "SELECT 1"]);

        Assert.IsNotNull(result);
        Assert.AreEqual("C:/data.db", result.Value.DbPath);
    }
}
