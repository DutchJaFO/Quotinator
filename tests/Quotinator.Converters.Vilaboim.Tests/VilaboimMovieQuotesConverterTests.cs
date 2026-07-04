using Quotinator.Converters.Vilaboim;
using Quotinator.Core.Import;
using Quotinator.Data.Import;

namespace Quotinator.Converters.Vilaboim.Tests;

[TestClass]
public class VilaboimMovieQuotesConverterTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string BaselineFile => Path.Combine(RepoRoot, "data", "sources", "vilaboim_movie-quotes.json");

    private string _tempDir = null!;

    [TestInitialize]
    public void TestInitialize()
        => _tempDir = Directory.CreateTempSubdirectory("quotinator_vilaboim_test_").FullName;

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // The single most important test in this project: proves the live converter produces the exact
    // same id the committed, already-shipped canonical file already has for this quote/source pair.
    // If this ever fails, a live re-conversion would silently duplicate/orphan existing production data.
    [TestMethod]
    public async Task ConvertAsync_KnownQuoteSourcePair_MatchesCommittedBaselineId()
    {
        var expectedId = FindBaselineId("Frankly, my dear, I don't give a damn.", "Gone with the Wind");
        var inputPath  = WriteInput("[\"\\\"Frankly, my dear, I don't give a damn.\\\" Gone with the Wind\"]");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new VilaboimMovieQuotesConverter().ConvertAsync(inputPath, outputPath);

        var text = await File.ReadAllTextAsync(outputPath);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out var quotes));
        Assert.AreEqual(expectedId, quotes!.Single().Id);
    }

    [TestMethod]
    public async Task ConvertAsync_MultipleEntries_ParsesAll()
    {
        var inputPath  = WriteInput("""
            ["\"Quote one.\" Source One", "\"Quote two.\" Source Two"]
            """);
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new VilaboimMovieQuotesConverter().ConvertAsync(inputPath, outputPath);

        var text = await File.ReadAllTextAsync(outputPath);
        SourceQuoteFileReader.TryParse(text, out var quotes);
        Assert.AreEqual(2, quotes!.Count);
    }

    [TestMethod]
    public async Task ConvertAsync_OneMalformedEntry_SkipsItButConvertsTheRest()
    {
        var inputPath  = WriteInput("""
            ["this entry does not match the pattern at all", "\"A real quote.\" A Real Source"]
            """);
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new VilaboimMovieQuotesConverter().ConvertAsync(inputPath, outputPath);

        var text = await File.ReadAllTextAsync(outputPath);
        SourceQuoteFileReader.TryParse(text, out var quotes);
        Assert.AreEqual(1, quotes!.Count);
        Assert.AreEqual("A real quote.", quotes[0].QuoteText);
    }

    [TestMethod]
    public async Task ConvertAsync_ZeroEntriesParse_ThrowsSourceConversionException()
    {
        var inputPath = WriteInput("[\"nothing here matches\"]");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new VilaboimMovieQuotesConverter().ConvertAsync(inputPath, outputPath));
    }

    [TestMethod]
    public async Task ConvertAsync_InvalidTopLevelJson_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("{ this is not an array");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new VilaboimMovieQuotesConverter().ConvertAsync(inputPath, outputPath));
    }

    [TestMethod]
    public async Task ConvertAsync_Output_HasFixedCanonicalFields()
    {
        var inputPath  = WriteInput("[\"\\\"A quote.\\\" A Source\"]");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new VilaboimMovieQuotesConverter().ConvertAsync(inputPath, outputPath);

        var text = await File.ReadAllTextAsync(outputPath);
        SourceQuoteFileReader.TryParse(text, out var quotes);
        var quote = quotes!.Single();

        Assert.AreEqual("movie", quote.Type);
        Assert.AreEqual("en", quote.OriginalLanguage);
        Assert.IsNull(quote.Date);
        Assert.IsNull(quote.Character);
        Assert.IsNull(quote.Author);
        Assert.AreEqual(0, quote.Genres.Count);
        Assert.AreEqual(0, quote.Translations.Count);
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
