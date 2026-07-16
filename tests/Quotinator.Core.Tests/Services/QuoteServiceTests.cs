using Quotinator.Core.Services;

namespace Quotinator.Core.Tests.Services;

[TestClass]
public class QuoteServiceTests
{
    private static readonly string SourcesDir =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "sources"));

    private static IEnumerable<string> SourceFiles =>
        Directory.Exists(SourcesDir)
            ? Directory.EnumerateFiles(SourcesDir, "*.json")
                       .Where(f => !Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            : [];

    /// <summary>
    /// Each committed source file exists, is valid JSON, and contains at least one quote — except
    /// quotinator-series-universe.json (#180), whose entire purpose is Source/Series/Universe
    /// enrichment with a deliberately empty quotes[] section.
    /// </summary>
    [TestMethod]
    public void Load_EachSourceFile_ReturnsQuotes()
    {
        Assert.IsTrue(Directory.Exists(SourcesDir), $"data/sources/ not found at: {SourcesDir}");

        var files = SourceFiles
            .Where(f => !Path.GetFileName(f).Equals("quotinator-series-universe.json", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.IsTrue(files.Count > 0, "data/sources/ contains no JSON source files");

        foreach (var file in files)
        {
            var service = new QuoteService(file);
            var result  = service.GetAll(1, 10);
            Assert.IsGreaterThan(0, result.TotalCount, $"No quotes loaded from: {Path.GetFileName(file)}");
        }
    }

    /// <summary>Every entry in every source file has a non-empty id, quote, and source.</summary>
    [TestMethod]
    public void Load_EachSourceFile_AllEntriesHaveRequiredFields()
    {
        foreach (var file in SourceFiles)
        {
            var service = new QuoteService(file);
            var result  = service.GetAll(1, int.MaxValue);

            foreach (var q in result.Items)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(q.Id),     $"[{Path.GetFileName(file)}] Empty id on: {q.Quote[..20]}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(q.Quote),  $"[{Path.GetFileName(file)}] Empty quote text");
                Assert.IsFalse(string.IsNullOrWhiteSpace(q.Source), $"[{Path.GetFileName(file)}] Empty source on: {q.Quote[..20]}");
            }
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
