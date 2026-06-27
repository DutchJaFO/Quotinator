using Quotinator.Data.Entities;
using Quotinator.Core.Helpers;

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

    #region ValidTypes

    [TestMethod]
    [DataRow("movie")]
    [DataRow("tv")]
    [DataRow("anime")]
    [DataRow("book")]
    [DataRow("person")]
    public void ValidTypes_ContainsExpectedValues(string type)
    {
        Assert.IsTrue(InputValidation.ValidTypes.Contains(type));
    }

    [TestMethod]
    public void ValidTypes_DoesNotContainUnknownValues()
    {
        Assert.IsFalse(InputValidation.ValidTypes.Contains("film"));
        Assert.IsFalse(InputValidation.ValidTypes.Contains("Movie")); // case-sensitive
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
        Assert.IsTrue(InputValidation.ValidSearchFields.Contains(field));
    }

    [TestMethod]
    public void ValidSearchFields_DoesNotContainUnknownValues()
    {
        Assert.IsFalse(InputValidation.ValidSearchFields.Contains("genre"));
        Assert.IsFalse(InputValidation.ValidSearchFields.Contains("Quote")); // case-sensitive
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
        Assert.IsTrue(InputValidation.ValidGenres.Contains(genre));
    }

    [TestMethod]
    public void ValidGenres_DoesNotContainUnknownValues()
    {
        Assert.IsFalse(InputValidation.ValidGenres.Contains("scifi"));    // missing hyphen
        Assert.IsFalse(InputValidation.ValidGenres.Contains("SciFi"));    // wrong casing
        Assert.IsFalse(InputValidation.ValidGenres.Contains("cartoon"));
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
    [DataRow("'; DROP TABLE Quotes --")]
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
