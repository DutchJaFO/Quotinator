using Quotinator.Converters.NikhilNamal17;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Converters.NikhilNamal17.Tests;

[TestClass]
public class NikhilNamal17PopularMovieQuotesConverterTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string BaselineFile =>
        Path.Combine(RepoRoot, "data", "sources", "NikhilNamal17_popular-movie-quotes.json");

    private string _tempDir = null!;

    [TestInitialize]
    public void TestInitialize()
        => _tempDir = Directory.CreateTempSubdirectory("quotinator_nikhilnamal17_test_").FullName;

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // The single most important test in this project: proves the live converter produces the exact
    // same id the committed, already-shipped canonical file already has for this quote/source pair.
    [TestMethod]
    public async Task ConvertAsync_KnownQuoteSourcePair_MatchesCommittedBaselineId()
    {
        var expectedId = FindBaselineId("Do, or do not. There is no try.", "Star Wars: Episode V - The Empire Strikes Back");
        var inputPath  = WriteInput("""
            [{"quote":"Do, or do not. There is no try.","movie":"Star Wars: Episode V - The Empire Strikes Back","type":"movie","year":1980}]
            """);
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new NikhilNamal17PopularMovieQuotesConverter().ConvertAsync(inputPath, outputPath);

        var text = await File.ReadAllTextAsync(outputPath);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out var quotes));
        Assert.AreEqual(expectedId, quotes!.Single().Id);
    }

    [TestMethod]
    public async Task ConvertAsync_NumericYear_NormalisedToString()
    {
        var inputPath  = WriteInput("""
            [{"quote":"A quote.","movie":"A Movie","type":"movie","year":1994}]
            """);
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new NikhilNamal17PopularMovieQuotesConverter().ConvertAsync(inputPath, outputPath);

        SourceQuoteFileReader.TryParse(await File.ReadAllTextAsync(outputPath), out var quotes);
        Assert.AreEqual("1994", quotes!.Single().Date);
    }

    [TestMethod]
    public async Task ConvertAsync_StringYear_NormalisedToString()
    {
        var inputPath  = WriteInput("""
            [{"quote":"A quote.","movie":"A Movie","type":"movie","year":"1994"}]
            """);
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new NikhilNamal17PopularMovieQuotesConverter().ConvertAsync(inputPath, outputPath);

        SourceQuoteFileReader.TryParse(await File.ReadAllTextAsync(outputPath), out var quotes);
        Assert.AreEqual("1994", quotes!.Single().Date);
    }

    [TestMethod]
    public async Task ConvertAsync_OutOfRangeYear_ResultsInNullDate()
    {
        var inputPath  = WriteInput("""
            [{"quote":"A quote.","movie":"A Movie","type":"movie","year":1899}]
            """);
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new NikhilNamal17PopularMovieQuotesConverter().ConvertAsync(inputPath, outputPath);

        SourceQuoteFileReader.TryParse(await File.ReadAllTextAsync(outputPath), out var quotes);
        Assert.IsNull(quotes!.Single().Date);
    }

    [TestMethod]
    public async Task ConvertAsync_UnrecognisedType_FallsBackToDefault()
    {
        var inputPath  = WriteInput("""
            [{"quote":"A quote.","movie":"A Movie","type":"podcast","year":2000}]
            """);
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new NikhilNamal17PopularMovieQuotesConverter().ConvertAsync(inputPath, outputPath);

        SourceQuoteFileReader.TryParse(await File.ReadAllTextAsync(outputPath), out var quotes);
        Assert.AreEqual(QuoteType.Movie, quotes!.Single().Type);
    }

    [TestMethod]
    public async Task ConvertAsync_EmptyQuoteOrMovie_EntrySkippedButRestConverted()
    {
        var inputPath  = WriteInput("""
            [{"quote":"","movie":"A Movie","type":"movie","year":2000},
             {"quote":"A real quote.","movie":"A Real Movie","type":"movie","year":2000}]
            """);
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new NikhilNamal17PopularMovieQuotesConverter().ConvertAsync(inputPath, outputPath);

        SourceQuoteFileReader.TryParse(await File.ReadAllTextAsync(outputPath), out var quotes);
        Assert.AreEqual(1, quotes!.Count);
        Assert.AreEqual("A real quote.", quotes[0].QuoteText);
    }

    [TestMethod]
    public async Task ConvertAsync_ZeroValidEntries_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("""[{"quote":"","movie":""}]""");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new NikhilNamal17PopularMovieQuotesConverter().ConvertAsync(inputPath, outputPath));
    }

    [TestMethod]
    public async Task ConvertAsync_InvalidTopLevelJson_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("{ this is not an array");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new NikhilNamal17PopularMovieQuotesConverter().ConvertAsync(inputPath, outputPath));
    }

    private string WriteInput(string content)
    {
        var path = Path.Combine(_tempDir, "input.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static string FindBaselineId(string quote, string source)
    {
        var text = File.ReadAllText(BaselineFile);
        SourceQuoteFileReader.TryParse(text, out var quotes);
        return quotes!.Single(q => q.QuoteText == quote && q.Source == source).Id;
    }
}
