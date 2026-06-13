using Quotinator.Core.Services;

namespace Quotinator.Core.Tests.Services;

[TestClass]
public class QuoteServiceTests
{
    private static readonly string DataPath =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "quotes.json"));

    /// <summary>The committed quotes.json exists and is valid JSON.</summary>
    [TestMethod]
    public void Load_RealDataFile_ReturnsQuotes()
    {
        Assert.IsTrue(File.Exists(DataPath), $"data/quotes.json not found at: {DataPath}");

        var service = new QuoteService(DataPath);
        var result  = service.GetAll(1, 10);

        Assert.IsGreaterThan(0, result.TotalCount);
    }

    /// <summary>Every entry has a non-empty id, quote, and source.</summary>
    [TestMethod]
    public void Load_RealDataFile_AllEntriesHaveRequiredFields()
    {
        var service = new QuoteService(DataPath);
        var result  = service.GetAll(1, int.MaxValue);

        foreach (var q in result.Items)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(q.Id),     $"Empty id on: {q.Quote[..20]}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(q.Quote),  "Empty quote text");
            Assert.IsFalse(string.IsNullOrWhiteSpace(q.Source), $"Empty source on: {q.Quote[..20]}");
        }
    }

    /// <summary>Missing file returns empty list rather than throwing.</summary>
    [TestMethod]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var service = new QuoteService("does-not-exist.json");
        var result  = service.GetAll(1, 10);

        Assert.AreEqual(0, result.TotalCount);
    }
}
