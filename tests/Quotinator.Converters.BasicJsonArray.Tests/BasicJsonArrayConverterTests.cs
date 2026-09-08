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

    /// <summary>
    /// The single most important test in this project: the generic converter, configured to reproduce
    /// NikhilNamal17's raw shape, produces the exact id this quote/source pair already carries in every
    /// shipped database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The id is pinned as a literal, and that is the assertion.</b> It was previously read out of
    /// <c>data/sources/NikhilNamal17_popular-movie-quotes.json</c> at run time, which made the test
    /// self-referential: regenerate that file under a changed id algorithm and both sides move
    /// together, so the one thing it exists to catch — an id change that orphans every existing row —
    /// passes silently. A literal cannot move.
    /// </para>
    /// <para>
    /// It also removed the last dependency this suite had on bundled data. Developer rule, 2026-09-08:
    /// <em>tests should not rely on bundled data to stay green, with the exception of the feature smoke
    /// test that exists purely to be aware of changes in the external data.</em> That exception is
    /// <c>import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions.md</c>; this is a unit
    /// test and gets a fixture.
    /// </para>
    /// <para>
    /// <b>If this fails, do not update the literal.</b> It means `QuoteIdentity.StableId`'s inputs or
    /// normalisation changed, and every id in every deployed database is about to be orphaned — which
    /// needs a migration and a decision, not a new expected value.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ConvertAsync_AgainstCommittedNikhilNamal17Fixture_IdsMatchExactly()
    {
        const string expectedId = "62465c4f-d0c1-2648-a658-7bc8e4f70b0f";
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
