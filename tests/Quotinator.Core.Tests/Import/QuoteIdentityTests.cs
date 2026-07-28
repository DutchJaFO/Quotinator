using Quotinator.Core.Import;

namespace Quotinator.Core.Tests.Import;

[TestClass]
public class QuoteIdentityTests
{
    // Pinned against the real, currently-committed data/sources/vilaboim_movie-quotes.json entry —
    // if this ever fails, the id-generation algorithm has drifted and would silently
    // duplicate/orphan existing production data on the next live conversion.
    [TestMethod]
    public void StableId_KnownQuoteSourcePair_MatchesCommittedProductionId()
    {
        var id = QuoteIdentity.StableId("Frankly, my dear, I don't give a damn.", "Gone with the Wind");

        Assert.AreEqual("1aa241c0-9a8f-e348-9d67-fdae91c0f33b", id);
    }

    [TestMethod]
    public void StableId_SameInput_IsDeterministic()
    {
        var first  = QuoteIdentity.StableId("Some quote.", "Some Source");
        var second = QuoteIdentity.StableId("Some quote.", "Some Source");

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void StableId_DifferentInputs_NeverCollide()
    {
        var a = QuoteIdentity.StableId("Quote A", "Source A");
        var b = QuoteIdentity.StableId("Quote B", "Source B");

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void StableId_WhitespaceAndCasingDifferences_NormaliseToSameId()
    {
        var a = QuoteIdentity.StableId("Frankly, my dear, I don't give a damn.", "Gone with the Wind");
        var b = QuoteIdentity.StableId("  FRANKLY,   my dear,   I don't give a damn.  ", "  gone WITH the wind  ");

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void Normalise_TrimsLowercasesAndCollapsesWhitespace()
    {
        var result = QuoteIdentity.Normalise("  Hello   World  ");

        Assert.AreEqual("hello world", result);
    }
}
