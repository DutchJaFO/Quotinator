using System.Text.Json;
using Quotinator.Converters.BasicJsonArray;
using Quotinator.Core.Enums;
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

    private static readonly string[] DramaSciFiGenres = ["drama", "sci-fi"];
    private static readonly string[] DramaGenre        = ["drama"];

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
        string inputPath  = WriteInput("""[{"quote":"A quote.","source":"A Source","type":"book"}]""");
        string outputPath = Path.Combine(_tempDir, "output.json");

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, cancellationToken: TestContext.CancellationToken);

        SourceQuoteDto quote = await ReadSingle(outputPath);
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
        string inputPath  = WriteInput("""[{"quote":"A quote.","movie":"A Source"}]""");
        string outputPath = Path.Combine(_tempDir, "output.json");
        JsonElement options = ToOptions(new BasicJsonArrayConverterOptionsDto
        {
            PropertyMapping = new NamedFieldMapping { Source = "movie" }
        });

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, options, TestContext.CancellationToken);

        SourceQuoteDto quote = await ReadSingle(outputPath);
        Assert.AreEqual("A Source", quote.Source);
    }

    [TestMethod]
    public async Task ConvertAsync_Defaults_PopulatesUnmappedField()
    {
        string inputPath  = WriteInput("""[{"quote":"A quote.","source":"A Source"}]""");
        string outputPath = Path.Combine(_tempDir, "output.json");
        JsonElement options = ToOptions(new BasicJsonArrayConverterOptionsDto
        {
            Defaults = new QuoteFieldDefaults { OriginalLanguage = "nl" }
        });

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, options, TestContext.CancellationToken);

        SourceQuoteDto quote = await ReadSingle(outputPath);
        Assert.AreEqual("nl", quote.OriginalLanguage);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Genres

    [TestMethod]
    public async Task ConvertAsync_GenresAsArray_ProducesMultipleGenres()
    {
        string inputPath  = WriteInput("""[{"quote":"A quote.","source":"A Source","genres":["drama","sci-fi"]}]""");
        string outputPath = Path.Combine(_tempDir, "output.json");

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, cancellationToken: TestContext.CancellationToken);

        SourceQuoteDto quote = await ReadSingle(outputPath);
        Assert.AreSequenceEqual(DramaSciFiGenres, quote.Genres);
    }

    [TestMethod]
    public async Task ConvertAsync_GenresAsSingleString_ProducesOneGenre()
    {
        string inputPath  = WriteInput("""[{"quote":"A quote.","source":"A Source","genres":"drama"}]""");
        string outputPath = Path.Combine(_tempDir, "output.json");

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, cancellationToken: TestContext.CancellationToken);

        SourceQuoteDto quote = await ReadSingle(outputPath);
        Assert.AreSequenceEqual(DramaGenre, quote.Genres);
    }

    [TestMethod]
    public async Task ConvertAsync_GenresAbsent_ProducesEmptyList()
    {
        string inputPath  = WriteInput("""[{"quote":"A quote.","source":"A Source"}]""");
        string outputPath = Path.Combine(_tempDir, "output.json");

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, cancellationToken: TestContext.CancellationToken);

        SourceQuoteDto quote = await ReadSingle(outputPath);
        Assert.IsEmpty(quote.Genres);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Errors

    [TestMethod]
    public async Task ConvertAsync_RowMissingQuoteOrSource_SkipsRow()
    {
        string inputPath  = WriteInput("""
            [{"quote":"","source":"A Source"},
             {"quote":"A real quote.","source":"A Real Source"}]
            """);
        string outputPath = Path.Combine(_tempDir, "output.json");

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, cancellationToken: TestContext.CancellationToken);

        string text = await File.ReadAllTextAsync(outputPath, TestContext.CancellationToken);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out List<SourceQuoteDto>? quotes));
        Assert.HasCount(1, quotes!);
        Assert.AreEqual("A real quote.", quotes![0].QuoteText);
    }

    [TestMethod]
    public async Task ConvertAsync_InvalidJson_ThrowsSourceConversionException()
    {
        string inputPath  = WriteInput("{ this is not an array");
        string outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ConvertAsync_ZeroValidEntries_ThrowsSourceConversionException()
    {
        string inputPath  = WriteInput("""[{"quote":"","source":""}]""");
        string outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, cancellationToken: TestContext.CancellationToken));
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
        string expectedId = FindBaselineId("Do, or do not. There is no try.", "Star Wars: Episode V - The Empire Strikes Back");
        string inputPath  = WriteInput("""
            [{"quote":"Do, or do not. There is no try.","movie":"Star Wars: Episode V - The Empire Strikes Back","type":"movie","year":1980}]
            """);
        string outputPath = Path.Combine(_tempDir, "output.json");
        JsonElement options = ToOptions(new BasicJsonArrayConverterOptionsDto
        {
            PropertyMapping = new NamedFieldMapping { Source = "movie", Date = "year" }
        });

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, options, TestContext.CancellationToken);

        string text = await File.ReadAllTextAsync(outputPath, TestContext.CancellationToken);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out List<SourceQuoteDto>? quotes));
        Assert.AreEqual(expectedId, quotes!.Single().Id);
    }

    [TestMethod]
    public async Task ConvertAsync_NumericYear_NormalisedToString()
    {
        string inputPath  = WriteInput("""[{"quote":"A quote.","movie":"A Movie","year":1994}]""");
        string outputPath = Path.Combine(_tempDir, "output.json");
        JsonElement options = ToOptions(new BasicJsonArrayConverterOptionsDto
        {
            PropertyMapping = new NamedFieldMapping { Source = "movie", Date = "year" }
        });

        await new BasicJsonArrayConverter().ConvertAsync(inputPath, outputPath, options, TestContext.CancellationToken);

        SourceQuoteDto quote = await ReadSingle(outputPath);
        Assert.AreEqual("1994", quote.Date);
    }

    #endregion

    private string WriteInput(string content)
    {
        string path = Path.Combine(_tempDir, "input.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static string FindBaselineId(string quote, string source)
    {
        string text = File.ReadAllText(BaselineFile);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out List<SourceQuoteDto>? quotes));
        return quotes!.Single(q => q.QuoteText == quote && q.Source == source).Id;
    }

    private static async Task<SourceQuoteDto> ReadSingle(string outputPath)
    {
        string text = await File.ReadAllTextAsync(outputPath);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out List<SourceQuoteDto>? quotes));
        return quotes!.Single();
    }

    private static JsonElement ToOptions(BasicJsonArrayConverterOptionsDto options)
        => JsonSerializer.SerializeToElement(options);

    public TestContext TestContext { get; set; }
}
