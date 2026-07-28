using Quotinator.Core.Import;
using Quotinator.Core.Models;

namespace Quotinator.Core.Tests.Import;

[TestClass]
public class MappedSourceQuoteBuilderTests
{
    // -------------------------------------------------------------------------
    #region Resolve

    [TestMethod]
    public void Resolve_RawValuePresent_ReturnsRawValue()
        => Assert.AreEqual("raw", MappedSourceQuoteBuilder.Resolve(" raw ", "default"));

    [TestMethod]
    public void Resolve_RawValueEmpty_ReturnsDefault()
        => Assert.AreEqual("default", MappedSourceQuoteBuilder.Resolve("  ", "default"));

    [TestMethod]
    public void Resolve_RawValueNull_ReturnsDefault()
        => Assert.AreEqual("default", MappedSourceQuoteBuilder.Resolve(null, "default"));

    [TestMethod]
    public void Resolve_BothNull_ReturnsNull()
        => Assert.IsNull(MappedSourceQuoteBuilder.Resolve(null, null));

    #endregion

    // -------------------------------------------------------------------------
    #region Build — quote/source required

    [TestMethod]
    public void Build_QuoteEmpty_ReturnsNull()
        => Assert.IsNull(MappedSourceQuoteBuilder.Build(null, "  ", null, "A Source", null, null, null, null, null));

    [TestMethod]
    public void Build_SourceEmpty_ReturnsNull()
        => Assert.IsNull(MappedSourceQuoteBuilder.Build(null, "A quote.", null, "  ", null, null, null, null, null));

    [TestMethod]
    public void Build_QuoteAndSourcePresent_ReturnsQuote()
    {
        var quote = MappedSourceQuoteBuilder.Build(null, "A quote.", null, "A Source", null, null, null, null, null);
        Assert.IsNotNull(quote);
        Assert.AreEqual("A quote.", quote!.QuoteText);
        Assert.AreEqual("A Source", quote.Source);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Build — id derivation

    [TestMethod]
    public void Build_NoIdSupplied_DerivesStableId()
    {
        var quote = MappedSourceQuoteBuilder.Build(null, "A quote.", null, "A Source", null, null, null, null, null);
        Assert.AreEqual(QuoteIdentity.StableId("A quote.", "A Source"), quote!.Id);
    }

    [TestMethod]
    public void Build_ExplicitIdSupplied_TakesPrecedence()
    {
        var explicitId = Guid.NewGuid().ToString();
        var quote = MappedSourceQuoteBuilder.Build(explicitId, "A quote.", null, "A Source", null, null, null, null, null);
        Assert.AreEqual(explicitId, quote!.Id);
        Assert.AreNotEqual(QuoteIdentity.StableId("A quote.", "A Source"), quote.Id);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Build — fallbacks

    [TestMethod]
    public void Build_NoOriginalLanguageOrDefault_FallsBackToEn()
    {
        var quote = MappedSourceQuoteBuilder.Build(null, "A quote.", null, "A Source", null, null, null, null, null);
        Assert.AreEqual("en", quote!.OriginalLanguage);
    }

    [TestMethod]
    public void Build_NoTypeOrDefault_FallsBackToMovie()
    {
        var quote = MappedSourceQuoteBuilder.Build(null, "A quote.", null, "A Source", null, null, null, null, null);
        Assert.AreEqual(QuoteType.Movie, quote!.Type);
    }

    [TestMethod]
    public void Build_TypeRawRecognised_UsesRecognisedType()
    {
        var quote = MappedSourceQuoteBuilder.Build(null, "A quote.", null, "A Source", null, null, null, "book", null);
        Assert.AreEqual(QuoteType.Book, quote!.Type);
    }

    [TestMethod]
    public void Build_GenresNull_ReturnsEmptyList()
    {
        var quote = MappedSourceQuoteBuilder.Build(null, "A quote.", null, "A Source", null, null, null, null, null);
        Assert.AreEqual(0, quote!.Genres.Count);
    }

    [TestMethod]
    public void Build_GenresSupplied_ReturnedVerbatim()
    {
        var quote = MappedSourceQuoteBuilder.Build(null, "A quote.", null, "A Source", null, null, null, null, ["drama", "sci-fi"]);
        CollectionAssert.AreEqual(new[] { "drama", "sci-fi" }, quote!.Genres.ToList());
    }

    #endregion
}
