using Quotinator.Core.Enums;
using System.Text.Json;
using Quotinator.Core.Helpers;
using Quotinator.Core.Models;

namespace Quotinator.Core.Tests.Helpers;

[TestClass]
public class InputValidationTests
{
    #region IsValidLang

    [TestMethod]
    [DataRow("en")]
    [DataRow("nl")]
    [DataRow("de")]
    [DataRow("en-GB")]
    [DataRow("nl-BE")]
    [DataRow("zh-Hans")]
    [DataRow("EN")]
    [DataRow("EN-GB")]
    public void IsValidLang_ValidCodes_ReturnsTrue(string lang)
    {
        Assert.IsTrue(InputValidation.IsValidLang(lang));
    }

    [TestMethod]
    [DataRow("e")]           // too short
    [DataRow("english")]     // too long, no hyphen
    [DataRow("en_GB")]       // underscore not allowed
    [DataRow("en-TOOLONG")]  // region tag too long
    [DataRow("123")]         // digits not allowed
    [DataRow("en-1")]        // digit in region
    [DataRow("toolongcode")] // exceeds 8 chars
    [DataRow("")]            // empty
    public void IsValidLang_InvalidCodes_ReturnsFalse(string lang)
    {
        Assert.IsFalse(InputValidation.IsValidLang(lang));
    }

    #endregion

    #region TryNormalizeLang

    /// <summary>#216 fix: a mixed/upper-case lang value must be validated and lowercased in one step.</summary>
    [TestMethod]
    [DataRow("EN", "en")]
    [DataRow("nl", "nl")]
    [DataRow("En-Gb", "en-gb")]
    [DataRow("ZH-HANS", "zh-hans")]
    public void TryNormalizeLang_ValidCode_ReturnsTrueAndLowercases(string input, string expected)
    {
        string? lang = input;
        var ok = InputValidation.TryNormalizeLang(ref lang);

        Assert.IsTrue(ok);
        Assert.AreEqual(expected, lang);
    }

    [TestMethod]
    public void TryNormalizeLang_Null_ReturnsTrueAndLeavesNull()
    {
        string? lang = null;
        var ok = InputValidation.TryNormalizeLang(ref lang);

        Assert.IsTrue(ok);
        Assert.IsNull(lang);
    }

    [TestMethod]
    [DataRow("english")]
    [DataRow("en_GB")]
    [DataRow("123")]
    public void TryNormalizeLang_InvalidCode_ReturnsFalseAndLeavesUnchanged(string input)
    {
        string? lang = input;
        var ok = InputValidation.TryNormalizeLang(ref lang);

        Assert.IsFalse(ok);
        Assert.AreEqual(input, lang, "Must not mutate the value on a failed validation");
    }

    #endregion

    #region ValidTypes

    [TestMethod]
    [DataRow("movie")]
    [DataRow("tv")]
    [DataRow("anime")]
    [DataRow("book")]
    [DataRow("person")]
    public void ValidTypes_ContainsExpectedValues(string type)
    {
        Assert.Contains(type, InputValidation.ValidTypes);
    }

    [TestMethod]
    public void ValidTypes_DoesNotContainUnknownValues()
    {
        Assert.DoesNotContain("film", InputValidation.ValidTypes);
        Assert.DoesNotContain("Movie", InputValidation.ValidTypes); // case-sensitive
    }

    [TestMethod]
    public void ValidTypes_MatchesQuoteTypeEnumExactly()
    {
        var expected = Enum.GetValues<QuoteType>()
            .Where(t => t != QuoteType.Unknown)
            .Select(t => t.ToString().ToLowerInvariant());
        Assert.AreSequenceEqual([.. expected], [.. InputValidation.ValidTypes], Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    #endregion

    #region ValidSearchFields

    [TestMethod]
    [DataRow("quote")]
    [DataRow("source")]
    [DataRow("character")]
    [DataRow("author")]
    public void ValidSearchFields_ContainsExpectedValues(string field)
    {
        Assert.Contains(field, InputValidation.ValidSearchFields);
    }

    [TestMethod]
    public void ValidSearchFields_DoesNotContainUnknownValues()
    {
        Assert.DoesNotContain("genre", InputValidation.ValidSearchFields);
        Assert.DoesNotContain("Quote", InputValidation.ValidSearchFields); // case-sensitive
    }

    #endregion

    #region ValidGenres

    [TestMethod]
    [DataRow("action")]
    [DataRow("sci-fi")]
    [DataRow("non-fiction")]
    [DataRow("drama")]
    [DataRow("thriller")]
    public void ValidGenres_ContainsExpectedValues(string genre)
    {
        Assert.Contains(genre, InputValidation.ValidGenres);
    }

    [TestMethod]
    public void ValidGenres_DoesNotContainUnknownValues()
    {
        Assert.DoesNotContain("scifi", InputValidation.ValidGenres);    // missing hyphen
        Assert.DoesNotContain("SciFi", InputValidation.ValidGenres);    // wrong casing
        Assert.DoesNotContain("cartoon", InputValidation.ValidGenres);
    }

    [TestMethod]
    public void ValidGenres_MatchesGenreEnumExactly()
    {
        var expected = Enum.GetValues<Genre>()
            .Where(g => g != Genre.Unknown)
            .Select(g => JsonNamingPolicy.KebabCaseLower.ConvertName(g.ToString()));
        Assert.AreSequenceEqual([.. expected], [.. InputValidation.ValidGenres], Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    #endregion

    #region GenreApiToDb

    [TestMethod]
    public void GenreApiToDb_ContainsAllValidGenres()
    {
        foreach (var genre in InputValidation.ValidGenres)
            Assert.IsTrue(
                InputValidation.GenreApiToDb.ContainsKey(genre),
                $"GenreApiToDb is missing a mapping for valid genre '{genre}'");
    }

    [TestMethod]
    public void GenreApiToDb_AllMappedValuesAreValidEnumNames()
    {
        foreach (var (apiTag, dbName) in InputValidation.GenreApiToDb)
            Assert.IsTrue(
                Enum.TryParse<Genre>(dbName, out _),
                $"GenreApiToDb[\"{apiTag}\"] = \"{dbName}\" is not a valid Genre enum name");
    }

    [TestMethod]
    [DataRow("sci-fi",      "SciFi")]
    [DataRow("non-fiction", "NonFiction")]
    public void GenreApiToDb_HyphenatedGenresMappedCorrectly(string apiTag, string expectedDbName)
    {
        Assert.IsTrue(InputValidation.GenreApiToDb.TryGetValue(apiTag, out var actual),
            $"GenreApiToDb is missing key '{apiTag}'");
        Assert.AreEqual(expectedDbName, actual);
    }

    [TestMethod]
    public void GenreApiToDb_IsCaseInsensitive()
    {
        Assert.IsTrue(InputValidation.GenreApiToDb.ContainsKey("SCI-FI"));
        Assert.IsTrue(InputValidation.GenreApiToDb.ContainsKey("NON-FICTION"));
        Assert.IsTrue(InputValidation.GenreApiToDb.ContainsKey("Drama"));
    }

    #endregion

    #region IsSuspiciousInput

    [TestMethod]
    [DataRow("' OR 1=1 --")]
    [DataRow("'; DROP TABLE Quotinator_Quote --")]
    [DataRow("UNION SELECT * FROM Users")]
    [DataRow("/* comment */")]
    [DataRow("EXEC(xp_cmdshell)")]
    public void IsSuspiciousInput_KnownInjectionPatterns_ReturnsTrue(string value)
    {
        Assert.IsTrue(InputValidation.IsSuspiciousInput(value));
    }

    [TestMethod]
    [DataRow("Gandalf")]
    [DataRow("O'Brien")]           // apostrophe without OR/AND is fine
    [DataRow("Rick Blaine")]
    [DataRow("Winston Churchill")]
    [DataRow("The Lord of the Rings")]
    public void IsSuspiciousInput_NormalValues_ReturnsFalse(string value)
    {
        Assert.IsFalse(InputValidation.IsSuspiciousInput(value));
    }

    #endregion
}
