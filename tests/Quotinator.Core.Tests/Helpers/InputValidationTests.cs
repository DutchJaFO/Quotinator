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
}
