using System.Text.Json;
using Quotinator.Converters.BasicJsonArray;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Converters.BasicJsonArray.Tests;

[TestClass]
public class BasicJsonArrayConverterTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string BaselineFile =>
        Path.Combine(RepoRoot, "data", "sources", "NikhilNamal17_popular-movie-quotes.json");

    private string _tempDir = null!;

    [TestInitialize]
    public void TestInitialize()
        => _tempDir = Directory.CreateTempSubdirectory("quotinator_basicjsonarray_test_").FullName;

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    #region Zero-config (canonical property names)

    [TestMethod]
    public async Task ConvertAsync_CanonicalPropertyNames_NoOptionsNeeded()
    {
        var inputPath  = WriteInput("""[{"quote":"A quote.","source":"A Source","type":"book"}]""");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual("A quote.", quote.QuoteText);
        Assert.AreEqual("A Source", quote.Source);
        Assert.AreEqual(QuoteType.Book, quote.Type);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region PropertyMapping

    [TestMethod]
    public async Task ConvertAsync_PropertyMapping_RemapsField()
    {
        var inputPath  = WriteInput("""[{"quote":"A quote.","movie":"A Source"}]""");
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = ToOptions(new BasicJsonArrayConverterOptions
        {
            PropertyMapping = new NamedFieldMapping { Source = "movie" }
        });

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, options);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual("A Source", quote.Source);
    }

    [TestMethod]
    public async Task ConvertAsync_Defaults_PopulatesUnmappedField()
    {
        var inputPath  = WriteInput("""[{"quote":"A quote.","source":"A Source"}]""");
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = ToOptions(new BasicJsonArrayConverterOptions
        {
            Defaults = new QuoteFieldDefaults { OriginalLanguage = "nl" }
        });

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, options);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual("nl", quote.OriginalLanguage);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Genres

    [TestMethod]
    public async Task ConvertAsync_GenresAsArray_ProducesMultipleGenres()
    {
        var inputPath  = WriteInput("""[{"quote":"A quote.","source":"A Source","genres":["drama","sci-fi"]}]""");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath);

        var quote = await ReadSingle(outputPath);
        CollectionAssert.AreEqual(new[] { "drama", "sci-fi" }, quote.Genres.ToList());
    }

    [TestMethod]
    public async Task ConvertAsync_GenresAsSingleString_ProducesOneGenre()
    {
        var inputPath  = WriteInput("""[{"quote":"A quote.","source":"A Source","genres":"drama"}]""");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath);

        var quote = await ReadSingle(outputPath);
        CollectionAssert.AreEqual(new[] { "drama" }, quote.Genres.ToList());
    }

    [TestMethod]
    public async Task ConvertAsync_GenresAbsent_ProducesEmptyList()
    {
        var inputPath  = WriteInput("""[{"quote":"A quote.","source":"A Source"}]""");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual(0, quote.Genres.Count);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Errors

    [TestMethod]
    public async Task ConvertAsync_RowMissingQuoteOrSource_SkipsRow()
    {
        var inputPath  = WriteInput("""
            [{"quote":"","source":"A Source"},
             {"quote":"A real quote.","source":"A Real Source"}]
            """);
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath);

        var text = await File.ReadAllTextAsync(outputPath);
        SourceQuoteFileReader.TryParse(text, out var quotes);
        Assert.AreEqual(1, quotes!.Count);
        Assert.AreEqual("A real quote.", quotes[0].QuoteText);
    }

    [TestMethod]
    public async Task ConvertAsync_InvalidJson_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("{ this is not an array");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath));
    }

    [TestMethod]
    public async Task ConvertAsync_ZeroValidEntries_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("""[{"quote":"","source":""}]""");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath));
    }

    #endregion

    // -------------------------------------------------------------------------
    #region ID stability

    // The single most important test in this project: proves the generic converter, configured to
    // reproduce NikhilNamal17's raw shape, produces the exact same id the committed, already-shipped
    // canonical file already has for this quote/source pair.
    [TestMethod]
    public async Task ConvertAsync_AgainstCommittedNikhilNamal17Fixture_IdsMatchExactly()
    {
        var expectedId = FindBaselineId("Do, or do not. There is no try.", "Star Wars: Episode V - The Empire Strikes Back");
        var inputPath  = WriteInput("""
            [{"quote":"Do, or do not. There is no try.","movie":"Star Wars: Episode V - The Empire Strikes Back","type":"movie","year":1980}]
            """);
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = ToOptions(new BasicJsonArrayConverterOptions
        {
            PropertyMapping = new NamedFieldMapping { Source = "movie", Date = "year" }
        });

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, options);

        var text = await File.ReadAllTextAsync(outputPath);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out var quotes));
        Assert.AreEqual(expectedId, quotes!.Single().Id);
    }

    [TestMethod]
    public async Task ConvertAsync_NumericYear_NormalisedToString()
    {
        var inputPath  = WriteInput("""[{"quote":"A quote.","movie":"A Movie","year":1994}]""");
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = ToOptions(new BasicJsonArrayConverterOptions
        {
            PropertyMapping = new NamedFieldMapping { Source = "movie", Date = "year" }
        });

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, options);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual("1994", quote.Date);
    }

    #endregion

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

    private static async Task<SourceQuote> ReadSingle(string outputPath)
    {
        var text = await File.ReadAllTextAsync(outputPath);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out var quotes));
        return quotes!.Single();
    }

    private static JsonElement ToOptions(BasicJsonArrayConverterOptions options)
        => JsonSerializer.SerializeToElement(options);
}
