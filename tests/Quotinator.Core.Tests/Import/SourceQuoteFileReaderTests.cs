using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Core.Tests.Import;

[TestClass]
public class SourceQuoteFileReaderTests
{
    [TestMethod]
    public void TryParse_BareArray_ParsesEntries()
    {
        var json = """
            [{"id":"11111111-1111-1111-1111-111111111111","quote":"Hello","source":"World"}]
            """;

        var result = SourceQuoteFileReader.TryParse(json, out var quotes);

        Assert.IsTrue(result);
        Assert.AreEqual(1, quotes!.Count);
        Assert.AreEqual("Hello", quotes[0].QuoteText);
    }

    [TestMethod]
    public void TryParse_QuotesWrapper_ParsesEntries()
    {
        var json = """
            {"quotes":[{"id":"11111111-1111-1111-1111-111111111111","quote":"Hello","source":"World"}]}
            """;

        var result = SourceQuoteFileReader.TryParse(json, out var quotes);

        Assert.IsTrue(result);
        Assert.AreEqual(1, quotes!.Count);
    }

    [TestMethod]
    public void TryParse_ObjectWithoutQuotesKey_ReturnsTrueWithEmptyList()
    {
        var result = SourceQuoteFileReader.TryParse("{}", out var quotes);

        Assert.IsTrue(result);
        Assert.AreEqual(0, quotes!.Count);
    }

    [TestMethod]
    public void TryParse_InvalidJson_ReturnsFalse()
    {
        var result = SourceQuoteFileReader.TryParse("{ this is not valid json", out var quotes);

        Assert.IsFalse(result);
        Assert.IsNull(quotes);
    }

    [TestMethod]
    public void TryParse_EmptyString_ReturnsFalse()
    {
        var result = SourceQuoteFileReader.TryParse(string.Empty, out var quotes);

        Assert.IsFalse(result);
        Assert.IsNull(quotes);
    }

    [TestMethod]
    public void TryParse_ArrayEntryMissingRequiredField_ReturnsFalse()
    {
        // "quote" is required on SourceQuote; the raw upstream format this guards against
        // (e.g. a foreign, unconverted quote source) commonly lacks required canonical fields.
        var json = """
            [{"movie":"Gone with the Wind"}]
            """;

        var result = SourceQuoteFileReader.TryParse(json, out var quotes);

        Assert.IsFalse(result);
        Assert.IsNull(quotes);
    }

    // ── TryParseExtended (#68) ───────────────────────────────────────────────

    [TestMethod]
    public void TryParseExtended_BareArray_YieldsQuotesAndEmptyExtendedSections()
    {
        var json = """
            [{"id":"11111111-1111-1111-1111-111111111111","quote":"Hello","source":"World"}]
            """;

        var result = SourceQuoteFileReader.TryParseExtended(json, out var parsed);

        Assert.IsTrue(result);
        Assert.AreEqual(1, parsed!.Quotes.Count);
        Assert.AreEqual(0, parsed.Sources.Count);
        Assert.AreEqual(0, parsed.StageDirections.Count);
        Assert.AreEqual(0, parsed.SoundCues.Count);
        Assert.AreEqual(0, parsed.Conversations.Count);
    }

    [TestMethod]
    public void TryParseExtended_FullObject_ParsesAllFiveSections()
    {
        var json = """
            {
              "quotes": [{"id":"11111111-1111-1111-1111-111111111111","quote":"Hello","source":"World"}],
              "sources": [{"id":"55555555-5555-5555-5555-555555555555","title":"World","type":"movie","date":"1994"}],
              "stageDirections": [{"id":"22222222-2222-2222-2222-222222222222","text":"[EXT. AIRPORT - DAY]"}],
              "soundCues": [{"id":"33333333-3333-3333-3333-333333333333","text":"[rimshot]"}],
              "conversations": [{
                "id":"44444444-4444-4444-4444-444444444444",
                "lines":[
                  {"order":1,"type":"stage_direction","stageDirectionId":"22222222-2222-2222-2222-222222222222"},
                  {"order":2,"type":"quote","quoteId":"11111111-1111-1111-1111-111111111111"},
                  {"order":3,"type":"sound_cue","soundCueId":"33333333-3333-3333-3333-333333333333"}
                ]
              }]
            }
            """;

        var result = SourceQuoteFileReader.TryParseExtended(json, out var parsed);

        Assert.IsTrue(result);
        Assert.AreEqual(1, parsed!.Quotes.Count);
        Assert.AreEqual(1, parsed.Sources.Count);
        Assert.AreEqual("World", parsed.Sources[0].Title);
        Assert.AreEqual(QuoteType.Movie, parsed.Sources[0].Type);
        Assert.AreEqual("1994", parsed.Sources[0].Date.Value);
        Assert.AreEqual(1, parsed.StageDirections.Count);
        Assert.AreEqual("[EXT. AIRPORT - DAY]", parsed.StageDirections[0].Text);
        Assert.AreEqual(1, parsed.SoundCues.Count);
        Assert.AreEqual("[rimshot]", parsed.SoundCues[0].Text);
        Assert.AreEqual(1, parsed.Conversations.Count);

        var lines = parsed.Conversations[0].Lines;
        Assert.AreEqual(3, lines.Count);
        Assert.AreEqual(ConversationLineType.StageDirection, lines[0].Type);
        Assert.AreEqual("22222222-2222-2222-2222-222222222222", lines[0].StageDirectionId);
        Assert.AreEqual(ConversationLineType.Quote, lines[1].Type);
        Assert.AreEqual("11111111-1111-1111-1111-111111111111", lines[1].QuoteId);
        Assert.AreEqual(ConversationLineType.SoundCue, lines[2].Type);
        Assert.AreEqual("33333333-3333-3333-3333-333333333333", lines[2].SoundCueId);
    }

    [TestMethod]
    public void SourceQuoteFileReader_PeopleSection_ParsesCorrectly()
    {
        var json = """
            {
              "quotes": [{"id":"11111111-1111-1111-1111-111111111111","quote":"Hello","source":"World"}],
              "people": [{"id":"66666666-6666-6666-6666-666666666666","name":"Ada Lovelace","dateOfBirth":"1815-12-10","dateOfDeath":"1852-11-27"}]
            }
            """;

        var result = SourceQuoteFileReader.TryParseExtended(json, out var parsed);

        Assert.IsTrue(result);
        Assert.AreEqual(1, parsed!.People.Count);
        Assert.AreEqual("66666666-6666-6666-6666-666666666666", parsed.People[0].Id);
        Assert.AreEqual("Ada Lovelace", parsed.People[0].Name);
        Assert.AreEqual("1815-12-10", parsed.People[0].DateOfBirth.Value);
        Assert.AreEqual("1852-11-27", parsed.People[0].DateOfDeath.Value);
    }

    // ── #190: absent vs. explicit-null distinguishability ────────────────────

    [TestMethod]
    public void TryParseExtended_SourceDateAbsent_IsDistinguishableFromExplicitNull()
    {
        var absentJson = """
            {"quotes":[],"sources":[{"title":"World","type":"movie"}]}
            """;
        var explicitNullJson = """
            {"quotes":[],"sources":[{"title":"World","type":"movie","date":null}]}
            """;

        SourceQuoteFileReader.TryParseExtended(absentJson, out var absentResult);
        SourceQuoteFileReader.TryParseExtended(explicitNullJson, out var explicitNullResult);

        Assert.IsFalse(absentResult!.Sources[0].Date.HasValue, "Omitted 'date' must be Absent, not Of(null)");
        Assert.IsTrue(explicitNullResult!.Sources[0].Date.HasValue, "Explicit 'date: null' must be Of(null), not Absent");
        Assert.IsNull(explicitNullResult.Sources[0].Date.Value);
    }

    [TestMethod]
    public void TryParseExtended_SourceSeriesNameAbsent_IsDistinguishableFromExplicitNull()
    {
        var absentJson = """
            {"quotes":[],"sources":[{"title":"World","type":"movie"}]}
            """;
        var explicitNullJson = """
            {"quotes":[],"sources":[{"title":"World","type":"movie","seriesName":null}]}
            """;

        SourceQuoteFileReader.TryParseExtended(absentJson, out var absentResult);
        SourceQuoteFileReader.TryParseExtended(explicitNullJson, out var explicitNullResult);

        Assert.IsFalse(absentResult!.Sources[0].SeriesName.HasValue, "Omitted 'seriesName' must be Absent, not Of(null)");
        Assert.IsTrue(explicitNullResult!.Sources[0].SeriesName.HasValue, "Explicit 'seriesName: null' must be Of(null), not Absent");
        Assert.IsNull(explicitNullResult.Sources[0].SeriesName.Value);
    }

    [TestMethod]
    public void TryParseExtended_PersonDateOfBirthAbsent_IsDistinguishableFromExplicitNull()
    {
        var absentJson = """
            {"quotes":[],"people":[{"id":"66666666-6666-6666-6666-666666666666","name":"Ada Lovelace"}]}
            """;
        var explicitNullJson = """
            {"quotes":[],"people":[{"id":"66666666-6666-6666-6666-666666666666","name":"Ada Lovelace","dateOfBirth":null}]}
            """;

        SourceQuoteFileReader.TryParseExtended(absentJson, out var absentResult);
        SourceQuoteFileReader.TryParseExtended(explicitNullJson, out var explicitNullResult);

        Assert.IsFalse(absentResult!.People[0].DateOfBirth.HasValue, "Omitted 'dateOfBirth' must be Absent, not Of(null)");
        Assert.IsTrue(explicitNullResult!.People[0].DateOfBirth.HasValue, "Explicit 'dateOfBirth: null' must be Of(null), not Absent");
        Assert.IsNull(explicitNullResult.People[0].DateOfBirth.Value);
    }

    [TestMethod]
    public void TryParseExtended_ObjectWithNoExtendedSections_YieldsEmptyLists()
    {
        var json = """{"quotes":[]}""";

        var result = SourceQuoteFileReader.TryParseExtended(json, out var parsed);

        Assert.IsTrue(result);
        Assert.AreEqual(0, parsed!.Sources.Count);
        Assert.AreEqual(0, parsed.StageDirections.Count);
        Assert.AreEqual(0, parsed.SoundCues.Count);
        Assert.AreEqual(0, parsed.Conversations.Count);
        Assert.AreEqual(0, parsed.Series.Count);
        Assert.AreEqual(0, parsed.Universe.Count);
    }

    [TestMethod]
    public void SourceQuoteFileReader_SeriesAndUniverseSections_ParseIntoEntries()
    {
        var json = """
            {
              "quotes": [{"id":"11111111-1111-1111-1111-111111111111","quote":"Hello","source":"World"}],
              "sources": [{"id":"77777777-7777-7777-7777-777777777777","title":"World","type":"movie","seriesName":"World Series"}],
              "series": [{"name":"World Series","universeName":"World Universe"}],
              "universe": [{"name":"World Universe"}]
            }
            """;

        var result = SourceQuoteFileReader.TryParseExtended(json, out var parsed);

        Assert.IsTrue(result);
        Assert.AreEqual(1, parsed!.Series.Count);
        Assert.AreEqual("World Series", parsed.Series[0].Name);
        Assert.AreEqual("World Universe", parsed.Series[0].UniverseName);
        Assert.AreEqual(1, parsed.Universe.Count);
        Assert.AreEqual("World Universe", parsed.Universe[0].Name);
        Assert.AreEqual(1, parsed.Sources.Count);
        Assert.AreEqual("World Series", parsed.Sources[0].SeriesName.Value);
    }

    [TestMethod]
    public void TryParseExtended_InvalidJson_ReturnsFalse()
    {
        var result = SourceQuoteFileReader.TryParseExtended("{ not valid json", out var parsed);

        Assert.IsFalse(result);
        Assert.IsNull(parsed);
    }

    [TestMethod]
    public void TryParseExtended_CuratedFile_ParsesRealFourConversationsWithStageDirectionAndSoundCue()
    {
        var repoRoot    = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var curatedFile = Path.Combine(repoRoot, "data", "sources", "quotinator-curated.json");
        var json        = File.ReadAllText(curatedFile);

        var result = SourceQuoteFileReader.TryParseExtended(json, out var parsed);

        Assert.IsTrue(result);
        Assert.AreEqual(4, parsed!.Conversations.Count);
        Assert.IsGreaterThanOrEqualTo(1, parsed.StageDirections.Count);
        Assert.IsGreaterThanOrEqualTo(1, parsed.SoundCues.Count);
        Assert.IsTrue(parsed.Conversations.Any(c => c.Lines.Any(l => l.Type == ConversationLineType.StageDirection)),
            "At least one conversation should use a stage direction");
        Assert.IsTrue(parsed.Conversations.Any(c => c.Lines.Any(l => l.Type == ConversationLineType.SoundCue)),
            "At least one conversation should use a sound cue");
    }
}
