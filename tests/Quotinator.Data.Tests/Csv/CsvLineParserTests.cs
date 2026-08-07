using Quotinator.Data.Csv;

namespace Quotinator.Data.Tests.Csv;

[TestClass]
public class CsvLineParserTests
{
    [TestMethod]
    public void Parse_SimpleTwoRowCsv_ReturnsRowsOfFields()
    {
        var rows = CsvLineParser.Parse("a,b,c\r\n1,2,3\r\n");

        Assert.HasCount(2, rows);
        Assert.AreSequenceEqual(["a", "b", "c"], rows[0]);
        Assert.AreSequenceEqual(["1", "2", "3"], rows[1]);
    }

    [TestMethod]
    public void Parse_QuotedFieldWithEmbeddedComma_KeepsCommaInsideField()
    {
        var rows = CsvLineParser.Parse("\"hello, world\",b\r\n");

        Assert.AreSequenceEqual(["hello, world", "b"], rows[0]);
    }

    [TestMethod]
    public void Parse_QuotedFieldWithEscapedQuote_UnescapesToSingleQuote()
    {
        var rows = CsvLineParser.Parse("\"she said \"\"hi\"\"\"\r\n");

        Assert.AreEqual("she said \"hi\"", rows[0][0]);
    }

    [TestMethod]
    public void Parse_QuotedFieldWithEmbeddedNewline_KeepsNewlineInsideField()
    {
        var rows = CsvLineParser.Parse("\"line one\nline two\",b\r\n");

        Assert.HasCount(1, rows);
        Assert.AreEqual("line one\nline two", rows[0][0]);
    }

    [TestMethod]
    public void Parse_NoTrailingNewline_StillReturnsLastRow()
    {
        var rows = CsvLineParser.Parse("a,b");

        Assert.HasCount(1, rows);
        Assert.AreSequenceEqual(["a", "b"], rows[0]);
    }

    [TestMethod]
    public void Parse_EmptyString_ReturnsNoRows() =>
        Assert.IsEmpty(CsvLineParser.Parse(""));
}
