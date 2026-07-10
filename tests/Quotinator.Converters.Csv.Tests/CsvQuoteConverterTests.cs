using System.Text.Json;
using Quotinator.Converters.Csv;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Converters.Csv.Tests;

[TestClass]
public class CsvQuoteConverterTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void TestInitialize()
        => _tempDir = Directory.CreateTempSubdirectory("quotinator_csv_test_").FullName;

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task ConvertAsync_MinimalColumns_ParsesQuoteAndSource()
    {
        var inputPath  = WriteInput("quote,source\n\"A quote.\",A Source\n");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new CsvQuoteConverter().ConvertAsync(inputPath, outputPath);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual("A quote.", quote.QuoteText);
        Assert.AreEqual("A Source", quote.Source);
        Assert.AreEqual("en", quote.OriginalLanguage);
        Assert.AreEqual(QuoteType.Movie, quote.Type);
        Assert.IsNull(quote.Date);
        Assert.IsNull(quote.Character);
        Assert.IsNull(quote.Author);
        Assert.AreEqual(0, quote.Genres.Count);
    }

    [TestMethod]
    public async Task ConvertAsync_NoIdColumnValue_DerivesStableId()
    {
        var inputPath  = WriteInput("quote,source\nA quote.,A Source\n");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new CsvQuoteConverter().ConvertAsync(inputPath, outputPath);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual(QuoteIdentity.StableId("A quote.", "A Source"), quote.Id);
    }

    [TestMethod]
    public async Task ConvertAsync_ExplicitIdColumn_TakesPrecedenceOverDerivedId()
    {
        var explicitId = Guid.NewGuid().ToString();
        var inputPath  = WriteInput($"id,quote,source\n{explicitId},A quote.,A Source\n");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new CsvQuoteConverter().ConvertAsync(inputPath, outputPath);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual(explicitId, quote.Id);
        Assert.AreNotEqual(QuoteIdentity.StableId("A quote.", "A Source"), quote.Id);
    }

    [TestMethod]
    public async Task ConvertAsync_AllColumnsPopulated_MapsEveryField()
    {
        var inputPath = WriteInput(
            "id,quote,originalLanguage,source,date,character,author,type,genres\n" +
            "11111111-1111-4111-8111-111111111111,A quote.,nl,A Source,1994,A Character,An Author,book,drama;sci-fi\n");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new CsvQuoteConverter().ConvertAsync(inputPath, outputPath);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual("11111111-1111-4111-8111-111111111111", quote.Id);
        Assert.AreEqual("nl", quote.OriginalLanguage);
        Assert.AreEqual("1994", quote.Date);
        Assert.AreEqual("A Character", quote.Character);
        Assert.AreEqual("An Author", quote.Author);
        Assert.AreEqual(QuoteType.Book, quote.Type);
        CollectionAssert.AreEqual(new[] { "drama", "sci-fi" }, quote.Genres.ToList());
    }

    [TestMethod]
    public async Task ConvertAsync_QuotedFieldWithEmbeddedComma_ParsesAsSingleField()
    {
        var inputPath  = WriteInput("quote,source\n\"A quote, with a comma.\",A Source\n");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new CsvQuoteConverter().ConvertAsync(inputPath, outputPath);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual("A quote, with a comma.", quote.QuoteText);
    }

    [TestMethod]
    public async Task ConvertAsync_QuotedFieldWithEscapedQuote_UnescapesCorrectly()
    {
        var inputPath  = WriteInput("quote,source\n\"She said \"\"hello\"\".\",A Source\n");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new CsvQuoteConverter().ConvertAsync(inputPath, outputPath);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual("She said \"hello\".", quote.QuoteText);
    }

    [TestMethod]
    public async Task ConvertAsync_OneRowMissingSource_SkipsItButConvertsTheRest()
    {
        var inputPath  = WriteInput("quote,source\nMissing a source,\nA real quote.,A Real Source\n");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new CsvQuoteConverter().ConvertAsync(inputPath, outputPath);

        var text = await File.ReadAllTextAsync(outputPath);
        SourceQuoteFileReader.TryParse(text, out var quotes);
        Assert.AreEqual(1, quotes!.Count);
        Assert.AreEqual("A real quote.", quotes[0].QuoteText);
    }

    [TestMethod]
    public async Task ConvertAsync_HeaderOnlyNoDataRows_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("quote,source\n");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new CsvQuoteConverter().ConvertAsync(inputPath, outputPath));
    }

    [TestMethod]
    public async Task ConvertAsync_MissingRequiredColumns_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("character,author\nSome Character,Some Author\n");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new CsvQuoteConverter().ConvertAsync(inputPath, outputPath));
    }

    [TestMethod]
    public async Task ConvertAsync_AllRowsMissingRequiredFields_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("quote,source\n,\n,\n");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new CsvQuoteConverter().ConvertAsync(inputPath, outputPath));
    }

    [TestMethod]
    public async Task ConvertAsync_ColumnHeaderCasing_IsCaseInsensitive()
    {
        var inputPath  = WriteInput("QUOTE,SOURCE\nA quote.,A Source\n");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new CsvQuoteConverter().ConvertAsync(inputPath, outputPath);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual("A quote.", quote.QuoteText);
    }

    [TestMethod]
    public void IsInternalOnly_DefaultsToFalse()
        => Assert.IsFalse(((IQuoteSourceConverter)new CsvQuoteConverter()).IsInternalOnly);

    // -------------------------------------------------------------------------
    #region ColumnMapping / Defaults options

    [TestMethod]
    public async Task ConvertAsync_ColumnMapping_MapsColumnsByPosition()
    {
        // Header labels deliberately don't match canonical names — mapping must be used exclusively.
        var inputPath  = WriteInput("Text,Movie\nA quote.,A Source\n");
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = ToOptions(new CsvConverterOptions
        {
            ColumnMapping = new IndexedFieldMapping { Quote = 1, Source = 2 }
        });

        await new CsvQuoteConverter().ConvertAsync(inputPath, outputPath, options);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual("A quote.", quote.QuoteText);
        Assert.AreEqual("A Source", quote.Source);
    }

    [TestMethod]
    public async Task ConvertAsync_HasHeaderFalse_TreatsFirstRowAsData()
    {
        var inputPath  = WriteInput("A quote.,A Source\n");
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = ToOptions(new CsvConverterOptions
        {
            HasHeader     = false,
            ColumnMapping = new IndexedFieldMapping { Quote = 1, Source = 2 }
        });

        await new CsvQuoteConverter().ConvertAsync(inputPath, outputPath, options);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual("A quote.", quote.QuoteText);
    }

    [TestMethod]
    public async Task ConvertAsync_Defaults_PopulatesUnmappedField()
    {
        var inputPath  = WriteInput("quote,source\nA quote.,A Source\n");
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = ToOptions(new CsvConverterOptions
        {
            Defaults = new QuoteFieldDefaults { OriginalLanguage = "nl", Type = QuoteType.Book }
        });

        await new CsvQuoteConverter().ConvertAsync(inputPath, outputPath, options);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual("nl", quote.OriginalLanguage);
        Assert.AreEqual(QuoteType.Book, quote.Type);
    }

    [TestMethod]
    public async Task ConvertAsync_ColumnMappingWithRowValue_RowValueTakesPrecedenceOverDefault()
    {
        var inputPath  = WriteInput("A quote.,A Source,book\n");
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = ToOptions(new CsvConverterOptions
        {
            HasHeader     = false,
            ColumnMapping = new IndexedFieldMapping { Quote = 1, Source = 2, Type = 3 },
            Defaults      = new QuoteFieldDefaults { Type = QuoteType.Tv }
        });

        await new CsvQuoteConverter().ConvertAsync(inputPath, outputPath, options);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual(QuoteType.Book, quote.Type);
    }

    private static JsonElement ToOptions(CsvConverterOptions options)
        => JsonSerializer.SerializeToElement(options);

    #endregion

    private string WriteInput(string content)
    {
        var path = Path.Combine(_tempDir, "input.csv");
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task<SourceQuote> ReadSingle(string outputPath)
    {
        var text = await File.ReadAllTextAsync(outputPath);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out var quotes));
        return quotes!.Single();
    }
}
