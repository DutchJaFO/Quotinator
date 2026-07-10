using System.Text.Json;
using Quotinator.Converters.RegexArray;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Converters.RegexArray.Tests;

[TestClass]
public class RegexArrayConverterTests
{
    private const string VilaboimPattern = """^"(.+?)"\s+(.+)$""";

    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string VilaboimBaselineFile => Path.Combine(RepoRoot, "data", "sources", "vilaboim_movie-quotes.json");

    private string _tempDir = null!;

    [TestInitialize]
    public void TestInitialize()
        => _tempDir = Directory.CreateTempSubdirectory("quotinator_regexarray_test_").FullName;

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    #region Pattern + GroupMapping

    [TestMethod]
    public async Task ConvertAsync_PatternAndGroupMapping_ProducesExpectedQuotes()
    {
        var inputPath  = WriteInput("[\"\\\"A quote.\\\" A Source\"]");
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = VilaboimOptions();

        await new RegexArrayConverter().ConvertAsync(inputPath, outputPath, options);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual("A quote.", quote.QuoteText);
        Assert.AreEqual("A Source", quote.Source);
    }

    [TestMethod]
    public async Task ConvertAsync_MultipleEntries_ParsesAll()
    {
        var inputPath  = WriteInput("""
            ["\"Quote one.\" Source One", "\"Quote two.\" Source Two"]
            """);
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new RegexArrayConverter().ConvertAsync(inputPath, outputPath, VilaboimOptions());

        var text = await File.ReadAllTextAsync(outputPath);
        SourceQuoteFileReader.TryParse(text, out var quotes);
        Assert.AreEqual(2, quotes!.Count);
    }

    [TestMethod]
    public async Task ConvertAsync_Defaults_PopulatesUnmappedField()
    {
        var inputPath  = WriteInput("[\"\\\"A quote.\\\" A Source\"]");
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = JsonSerializer.SerializeToElement(new RegexArrayConverterOptions
        {
            Pattern      = VilaboimPattern,
            GroupMapping = new IndexedFieldMapping { Quote = 1, Source = 2 },
            Defaults     = new QuoteFieldDefaults { Type = QuoteType.Book }
        });

        await new RegexArrayConverter().ConvertAsync(inputPath, outputPath, options);

        var quote = await ReadSingle(outputPath);
        Assert.AreEqual(QuoteType.Book, quote.Type);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Errors

    [TestMethod]
    public async Task ConvertAsync_NoPattern_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("[\"\\\"A quote.\\\" A Source\"]");
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = JsonSerializer.SerializeToElement(new RegexArrayConverterOptions
        {
            GroupMapping = new IndexedFieldMapping { Quote = 1, Source = 2 }
        });

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new RegexArrayConverter().ConvertAsync(inputPath, outputPath, options));
    }

    [TestMethod]
    public async Task ConvertAsync_NoGroupMapping_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("[\"\\\"A quote.\\\" A Source\"]");
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = JsonSerializer.SerializeToElement(new RegexArrayConverterOptions { Pattern = VilaboimPattern });

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new RegexArrayConverter().ConvertAsync(inputPath, outputPath, options));
    }

    [TestMethod]
    public async Task ConvertAsync_NonMatchingEntry_SkipsIt()
    {
        var inputPath  = WriteInput("""
            ["this entry does not match the pattern at all", "\"A real quote.\" A Real Source"]
            """);
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new RegexArrayConverter().ConvertAsync(inputPath, outputPath, VilaboimOptions());

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
            () => new RegexArrayConverter().ConvertAsync(inputPath, outputPath, VilaboimOptions()));
    }

    [TestMethod]
    public async Task ConvertAsync_ZeroValidEntries_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("[\"nothing here matches\"]");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new RegexArrayConverter().ConvertAsync(inputPath, outputPath, VilaboimOptions()));
    }

    #endregion

    // -------------------------------------------------------------------------
    #region ID stability

    // The single most important test in this project: proves the generic converter, configured to
    // reproduce Vilaboim's raw shape, produces the exact same id the committed, already-shipped
    // canonical file already has for this quote/source pair.
    [TestMethod]
    public async Task ConvertAsync_AgainstCommittedVilaboimFixture_IdsMatchExactly()
    {
        var expectedId = FindBaselineId("Frankly, my dear, I don't give a damn.", "Gone with the Wind");
        var inputPath  = WriteInput("[\"\\\"Frankly, my dear, I don't give a damn.\\\" Gone with the Wind\"]");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new RegexArrayConverter().ConvertAsync(inputPath, outputPath, VilaboimOptions());

        var text = await File.ReadAllTextAsync(outputPath);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out var quotes));
        Assert.AreEqual(expectedId, quotes!.Single().Id);
    }

    #endregion

    private static JsonElement VilaboimOptions() => JsonSerializer.SerializeToElement(new RegexArrayConverterOptions
    {
        Pattern      = VilaboimPattern,
        GroupMapping = new IndexedFieldMapping { Quote = 1, Source = 2 }
    });

    private string WriteInput(string content)
    {
        var path = Path.Combine(_tempDir, "input.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static string FindBaselineId(string quote, string source)
    {
        var text = File.ReadAllText(VilaboimBaselineFile);
        SourceQuoteFileReader.TryParse(text, out var quotes);
        return quotes!.Single(q => q.QuoteText == quote && q.Source == source).Id;
    }

    private static async Task<SourceQuote> ReadSingle(string outputPath)
    {
        var text = await File.ReadAllTextAsync(outputPath);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out var quotes));
        return quotes!.Single();
    }
}
