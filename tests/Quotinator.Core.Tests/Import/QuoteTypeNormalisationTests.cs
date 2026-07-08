using Quotinator.Core.Import;
using Quotinator.Core.Models;

namespace Quotinator.Core.Tests.Import;

[TestClass]
public class QuoteTypeNormalisationTests
{
    [TestMethod]
    [DataRow("movie", QuoteType.Movie)]
    [DataRow("tv", QuoteType.Tv)]
    [DataRow("anime", QuoteType.Anime)]
    [DataRow("book", QuoteType.Book)]
    [DataRow("person", QuoteType.Person)]
    [DataRow("MOVIE", QuoteType.Movie)]
    [DataRow("Person", QuoteType.Person)]
    public void CanonicalType_RecognisedValue_MapsToCanonicalType(string raw, QuoteType expected)
        => Assert.AreEqual(expected, QuoteTypeNormalisation.CanonicalType(raw, QuoteType.Movie));

    [TestMethod]
    public void CanonicalType_UnrecognisedValue_FallsBackToDefault()
        => Assert.AreEqual(QuoteType.Movie, QuoteTypeNormalisation.CanonicalType("podcast", QuoteType.Movie));

    [TestMethod]
    public void CanonicalType_Null_FallsBackToDefault()
        => Assert.AreEqual(QuoteType.Movie, QuoteTypeNormalisation.CanonicalType(null, QuoteType.Movie));
}
