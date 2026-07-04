using Quotinator.Core.Import;

namespace Quotinator.Core.Tests.Import;

[TestClass]
public class SourceQuoteFileReaderTests
{
    [TestMethod]
    public void TryParse_BareArray_ParsesEntries()
    {
        var json = """
            [{"id":"11111111-1111-1111-1111-111111111111","quote":"Hello","source":"World"}]
            """;

        var result = SourceQuoteFileReader.TryParse(json, out var quotes);

        Assert.IsTrue(result);
        Assert.AreEqual(1, quotes!.Count);
        Assert.AreEqual("Hello", quotes[0].QuoteText);
    }

    [TestMethod]
    public void TryParse_QuotesWrapper_ParsesEntries()
    {
        var json = """
            {"quotes":[{"id":"11111111-1111-1111-1111-111111111111","quote":"Hello","source":"World"}]}
            """;

        var result = SourceQuoteFileReader.TryParse(json, out var quotes);

        Assert.IsTrue(result);
        Assert.AreEqual(1, quotes!.Count);
    }

    [TestMethod]
    public void TryParse_ObjectWithoutQuotesKey_ReturnsTrueWithEmptyList()
    {
        var result = SourceQuoteFileReader.TryParse("{}", out var quotes);

        Assert.IsTrue(result);
        Assert.AreEqual(0, quotes!.Count);
    }

    [TestMethod]
    public void TryParse_InvalidJson_ReturnsFalse()
    {
        var result = SourceQuoteFileReader.TryParse("{ this is not valid json", out var quotes);

        Assert.IsFalse(result);
        Assert.IsNull(quotes);
    }

    [TestMethod]
    public void TryParse_EmptyString_ReturnsFalse()
    {
        var result = SourceQuoteFileReader.TryParse(string.Empty, out var quotes);

        Assert.IsFalse(result);
        Assert.IsNull(quotes);
    }

    [TestMethod]
    public void TryParse_ArrayEntryMissingRequiredField_ReturnsFalse()
    {
        // "quote" is required on SourceQuote; the raw upstream format this guards against
        // (e.g. a foreign, unconverted quote source) commonly lacks required canonical fields.
        var json = """
            [{"movie":"Gone with the Wind"}]
            """;

        var result = SourceQuoteFileReader.TryParse(json, out var quotes);

        Assert.IsFalse(result);
        Assert.IsNull(quotes);
    }
}
