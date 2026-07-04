using Quotinator.Core.Import;

namespace Quotinator.Core.Tests.Import;

[TestClass]
public class QuoteTypeNormalisationTests
{
    [TestMethod]
    [DataRow("movie")]
    [DataRow("tv")]
    [DataRow("anime")]
    [DataRow("book")]
    [DataRow("person")]
    [DataRow("MOVIE")]
    [DataRow("Person")]
    public void CanonicalType_RecognisedValue_MapsToItselfLowercase(string raw)
        => Assert.AreEqual(raw.ToLowerInvariant(), QuoteTypeNormalisation.CanonicalType(raw, "movie"));

    [TestMethod]
    public void CanonicalType_UnrecognisedValue_FallsBackToDefault()
        => Assert.AreEqual("movie", QuoteTypeNormalisation.CanonicalType("podcast", "movie"));

    [TestMethod]
    public void CanonicalType_Null_FallsBackToDefault()
        => Assert.AreEqual("movie", QuoteTypeNormalisation.CanonicalType(null, "movie"));
}
