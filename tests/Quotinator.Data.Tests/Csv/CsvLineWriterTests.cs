using Quotinator.Data.Csv;

namespace Quotinator.Data.Tests.Csv;

[TestClass]
public class CsvLineWriterTests
{
    [TestMethod]
    public void Write_PlainFields_JoinsWithCommaAndCrlfTerminatesEachRow() =>
        Assert.AreEqual("a,b,c\r\n1,2,3\r\n", CsvLineWriter.Write([["a", "b", "c"], ["1", "2", "3"]]));

    [TestMethod]
    public void Write_FieldWithEmbeddedComma_IsQuoted() =>
        Assert.AreEqual("\"hello, world\",b\r\n", CsvLineWriter.Write([["hello, world", "b"]]));

    [TestMethod]
    public void Write_FieldWithEmbeddedQuote_IsQuotedAndQuoteIsDoubled() =>
        Assert.AreEqual("\"she said \"\"hi\"\"\"\r\n", CsvLineWriter.Write([["she said \"hi\""]]));

    [TestMethod]
    public void Write_FieldWithEmbeddedNewline_IsQuoted() =>
        Assert.AreEqual("\"line one\nline two\"\r\n", CsvLineWriter.Write([["line one\nline two"]]));

    [TestMethod]
    public void Write_NullField_WritesAsEmptyUnquotedField() =>
        Assert.AreEqual(",b\r\n", CsvLineWriter.Write([[null, "b"]]));

    [TestMethod]
    public void Write_PlainFieldWithNoSpecialCharacters_IsNotQuoted() =>
        Assert.AreEqual("plain\r\n", CsvLineWriter.Write([["plain"]]));

    [TestMethod]
    public void WriteThenParse_FieldsWithCommasQuotesAndNewlines_RoundTripsExactly()
    {
        List<List<string?>> original =
        [
            ["quoteText", "existingValue", "incomingValue"],
            ["Sample, with comma", "She said \"hi\"", "Line one\nLine two"],
        ];

        var csv    = CsvLineWriter.Write(original);
        var parsed = CsvLineParser.Parse(csv);

        Assert.HasCount(original.Count, parsed);
        for (var i = 0; i < original.Count; i++)
            Assert.AreSequenceEqual([.. original[i].Select(f => f ?? "")], parsed[i]);
    }
}
