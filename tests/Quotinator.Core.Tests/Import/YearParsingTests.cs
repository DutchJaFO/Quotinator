using Quotinator.Core.Import;

namespace Quotinator.Core.Tests.Import;

[TestClass]
public class YearParsingTests
{
    [TestMethod]
    public void CleanYear_Int_InRange_ReturnsString()
        => Assert.AreEqual("1994", YearParsing.CleanYear((int?)1994));

    [TestMethod]
    public void CleanYear_Int_TooLow_ReturnsNull()
        => Assert.IsNull(YearParsing.CleanYear((int?)1900));

    [TestMethod]
    public void CleanYear_Int_TooHigh_ReturnsNull()
        => Assert.IsNull(YearParsing.CleanYear((int?)2100));

    [TestMethod]
    public void CleanYear_Int_Null_ReturnsNull()
        => Assert.IsNull(YearParsing.CleanYear((int?)null));

    [TestMethod]
    public void CleanYear_String_InRange_ReturnsNormalisedString()
        => Assert.AreEqual("1994", YearParsing.CleanYear(" 1994 "));

    [TestMethod]
    public void CleanYear_String_OutOfRange_ReturnsNull()
        => Assert.IsNull(YearParsing.CleanYear("1899"));

    [TestMethod]
    public void CleanYear_String_Unparseable_ReturnsNull()
        => Assert.IsNull(YearParsing.CleanYear("not a year"));

    [TestMethod]
    public void CleanYear_String_Null_ReturnsNull()
        => Assert.IsNull(YearParsing.CleanYear((string?)null));
}
