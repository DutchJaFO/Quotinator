using Quotinator.Tools.DbInspector;

namespace Quotinator.Tools.DbInspector.Tests;

[TestClass]
public class TableFormatterTests
{
    [TestMethod]
    public void Format_NoRows_ReturnsNoRowsMessage()
    {
        var result = TableFormatter.Format([]);

        Assert.AreEqual("(no rows)", result);
    }

    [TestMethod]
    public void Format_SingleRow_IncludesHeaderAndValueLine()
    {
        IDictionary<string, object> row = new Dictionary<string, object> { ["Name"] = "curated.json", ["Type"] = "Seed" };

        var result = TableFormatter.Format([row]);
        var lines  = result.Split(Environment.NewLine);

        Assert.AreEqual(2, lines.Length);
        StringAssert.Contains(lines[0], "Name");
        StringAssert.Contains(lines[0], "Type");
        StringAssert.Contains(lines[1], "curated.json");
        StringAssert.Contains(lines[1], "Seed");
    }

    [TestMethod]
    public void Format_NullValue_RendersAsNullLiteral()
    {
        IDictionary<string, object> row = new Dictionary<string, object> { ["Url"] = null! };

        var result = TableFormatter.Format([row]);

        StringAssert.Contains(result, "NULL");
    }

    [TestMethod]
    public void Format_ColumnsPaddedToWidestValue()
    {
        IDictionary<string, object> shortRow = new Dictionary<string, object> { ["Name"] = "a" };
        IDictionary<string, object> longRow  = new Dictionary<string, object> { ["Name"] = "a-much-longer-value" };

        var result = TableFormatter.Format([shortRow, longRow]);
        var lines  = result.Split(Environment.NewLine);

        Assert.AreEqual(lines[0].Length, lines[1].Length, "Header and value line must be padded to the same width");
        Assert.AreEqual(lines[1].Length, lines[2].Length, "All value lines must be padded to the same width");
    }

    [TestMethod]
    public void Format_MultipleColumns_SeparatesWithTwoSpaces()
    {
        IDictionary<string, object> row = new Dictionary<string, object> { ["A"] = "1", ["B"] = "2" };

        var result = TableFormatter.Format([row]);

        StringAssert.Contains(result, "A  B");
    }
}
