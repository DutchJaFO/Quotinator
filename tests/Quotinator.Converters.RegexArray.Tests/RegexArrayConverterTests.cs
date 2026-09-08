using System.Text.Json;
using Quotinator.Converters.RegexArray;
using Quotinator.Core.Enums;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Converters.RegexArray.Tests;

[TestClass]
public class RegexArrayConverterTests
{
    private const string VilaboimPattern = """^"(.+?)"\s+(.+)$""";

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

        await new RegexArrayConverter().ConvertAsync(inputPath, outputPath, options, TestContext.CancellationToken);

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

        await new RegexArrayConverter().ConvertAsync(inputPath, outputPath, VilaboimOptions(), TestContext.CancellationToken);

        var text = await File.ReadAllTextAsync(outputPath, TestContext.CancellationToken);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out var quotes));
        Assert.HasCount(2, quotes!);
    }

    [TestMethod]
    public async Task ConvertAsync_Defaults_PopulatesUnmappedField()
    {
        var inputPath  = WriteInput("[\"\\\"A quote.\\\" A Source\"]");
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = JsonSerializer.SerializeToElement(new RegexArrayConverterOptionsDto
        {
            Pattern      = VilaboimPattern,
            GroupMapping = new IndexedFieldMapping { Quote = 1, Source = 2 },
            Defaults     = new QuoteFieldDefaults { Type = QuoteType.Book }
        });

        await new RegexArrayConverter().ConvertAsync(inputPath, outputPath, options, TestContext.CancellationToken);

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
        var options = JsonSerializer.SerializeToElement(new RegexArrayConverterOptionsDto
        {
            GroupMapping = new IndexedFieldMapping { Quote = 1, Source = 2 }
        });

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new RegexArrayConverter().ConvertAsync(inputPath, outputPath, options, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ConvertAsync_NoGroupMapping_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("[\"\\\"A quote.\\\" A Source\"]");
        var outputPath = Path.Combine(_tempDir, "output.json");
        var options = JsonSerializer.SerializeToElement(new RegexArrayConverterOptionsDto { Pattern = VilaboimPattern });

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new RegexArrayConverter().ConvertAsync(inputPath, outputPath, options, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ConvertAsync_NonMatchingEntry_SkipsIt()
    {
        var inputPath  = WriteInput("""
            ["this entry does not match the pattern at all", "\"A real quote.\" A Real Source"]
            """);
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new RegexArrayConverter().ConvertAsync(inputPath, outputPath, VilaboimOptions(), TestContext.CancellationToken);

        var text = await File.ReadAllTextAsync(outputPath, TestContext.CancellationToken);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out var quotes));
        Assert.HasCount(1, quotes!);
        Assert.AreEqual("A real quote.", quotes![0].QuoteText);
    }

    [TestMethod]
    public async Task ConvertAsync_InvalidJson_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("{ this is not an array");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new RegexArrayConverter().ConvertAsync(inputPath, outputPath, VilaboimOptions(), TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ConvertAsync_ZeroValidEntries_ThrowsSourceConversionException()
    {
        var inputPath  = WriteInput("[\"nothing here matches\"]");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await Assert.ThrowsExactlyAsync<SourceConversionException>(
            () => new RegexArrayConverter().ConvertAsync(inputPath, outputPath, VilaboimOptions(), TestContext.CancellationToken));
    }

    #endregion

    // -------------------------------------------------------------------------
    #region ID stability

    /// <summary>
    /// The single most important test in this project: the generic converter, configured to reproduce
    /// Vilaboim's raw shape, produces the exact id this quote/source pair already carries in every
    /// shipped database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The id is pinned as a literal, and that is the assertion.</b> It was previously read out of
    /// <c>data/sources/vilaboim_movie-quotes.json</c> at run time, which made the test
    /// self-referential: regenerate that file under a changed id algorithm and both sides move
    /// together, so the one thing it exists to catch passes silently. The same value is pinned
    /// independently by <c>QuoteIdentityTests.StableId_KnownQuoteSourcePair_MatchesCommittedProductionId</c>,
    /// which is what corroborates it.
    /// </para>
    /// <para>
    /// Developer rule, 2026-09-08: <em>tests should not rely on bundled data to stay green, with the
    /// exception of the feature smoke test that exists purely to be aware of changes in the external
    /// data.</em> That exception is
    /// <c>import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions.md</c>.
    /// </para>
    /// <para>
    /// <b>If this fails, do not update the literal</b> — it means every id in every deployed database
    /// is about to be orphaned, which needs a migration and a decision, not a new expected value.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ConvertAsync_AgainstCommittedVilaboimFixture_IdsMatchExactly()
    {
        const string expectedId = "1aa241c0-9a8f-e348-9d67-fdae91c0f33b";
        var inputPath  = WriteInput("[\"\\\"Frankly, my dear, I don't give a damn.\\\" Gone with the Wind\"]");
        var outputPath = Path.Combine(_tempDir, "output.json");

        await new RegexArrayConverter().ConvertAsync(inputPath, outputPath, VilaboimOptions(), TestContext.CancellationToken);

        var text = await File.ReadAllTextAsync(outputPath, TestContext.CancellationToken);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out var quotes));
        Assert.AreEqual(expectedId, quotes!.Single().Id);
    }

    #endregion

    private static JsonElement VilaboimOptions() => JsonSerializer.SerializeToElement(new RegexArrayConverterOptionsDto
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

    private static async Task<SourceQuoteDto> ReadSingle(string outputPath)
    {
        var text = await File.ReadAllTextAsync(outputPath);
        Assert.IsTrue(SourceQuoteFileReader.TryParse(text, out var quotes));
        return quotes!.Single();
    }

    public TestContext TestContext { get; set; }
}
