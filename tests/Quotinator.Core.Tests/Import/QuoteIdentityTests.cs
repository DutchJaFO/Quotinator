using Quotinator.Core.Import;

namespace Quotinator.Core.Tests.Import;

[TestClass]
public class QuoteIdentityTests
{
    /// <summary>
    /// Every id below is one this quote/source pair already carries in shipped databases, pinned as a
    /// literal rather than read back from <c>data/sources/</c> at run time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A single example could not tell a stable algorithm from a lucky one.</b> One pinned pair
    /// proves that pair still hashes the same; it says nothing about apostrophes, punctuation, length,
    /// or multi-word sources, any of which a change to <see cref="QuoteIdentity.Normalise"/> could
    /// break independently. The rows below span all of those.
    /// </para>
    /// <para>
    /// <b>If a row fails, do not update its expected value.</b> It means the id algorithm has drifted
    /// and every id in every deployed database is about to be orphaned — a migration and a decision,
    /// not a new literal. The pairs come from <c>vilaboim_movie-quotes.json</c>, whose ids are
    /// converter-generated through this very method; the curated file's ids are hand-authored and are
    /// deliberately not used here.
    /// </para>
    /// </remarks>
    [TestMethod]
    [DataRow("Frankly, my dear, I don't give a damn.", "Gone with the Wind", "1aa241c0-9a8f-e348-9d67-fdae91c0f33b", DisplayName = "apostrophe + comma")]
    [DataRow("I'm gonna make him an offer he can't refuse.", "The Godfather", "b30b80f0-21f4-7942-b3bd-9fc75ec6207e", DisplayName = "two apostrophes")]
    [DataRow("You don't understand! I coulda had class. I coulda been a contender. I could've been somebody, instead of a bum, which is what I am.", "On the Waterfront", "0f7f3ce6-b269-984d-bbd7-fdefd1596817", DisplayName = "long, multi-sentence")]
    [DataRow("Toto, I've a feeling we're not in Kansas anymore.", "The Wizard of Oz", "e0709ba1-6e31-0a4c-8824-e894f62f0514", DisplayName = "multi-word source")]
    [DataRow("Here's looking at you, kid.", "Casablanca", "fc60dfad-226f-4b41-b6b3-5b55eb8836e1", DisplayName = "single-word source")]
    [DataRow("Go ahead, make my day.", "Sudden Impact", "64287d95-076b-814f-8119-c1c7689eb32e", DisplayName = "short quote")]
    [DataRow("All right, Mr. DeMille, I'm ready for my close-up.", "Sunset Boulevard", "59bccdb5-2c2d-c947-85c8-4db600d6f840", DisplayName = "abbreviation + hyphen")]
    public void StableId_KnownQuoteSourcePair_MatchesCommittedProductionId(string quote, string source, string expectedId)
        => Assert.AreEqual(expectedId, QuoteIdentity.StableId(quote, source));

    /// <summary>The control the rows above need: they are not all the same id, so a method returning one constant could not pass them.</summary>
    [TestMethod]
    public void StableId_AcrossTheKnownPairs_ProducesDistinctIds()
    {
        string[] ids =
        [
            QuoteIdentity.StableId("Frankly, my dear, I don't give a damn.", "Gone with the Wind"),
            QuoteIdentity.StableId("I'm gonna make him an offer he can't refuse.", "The Godfather"),
            QuoteIdentity.StableId("Toto, I've a feeling we're not in Kansas anymore.", "The Wizard of Oz"),
            QuoteIdentity.StableId("Here's looking at you, kid.", "Casablanca"),
        ];

        Assert.HasCount(ids.Length, ids.Distinct().ToArray(), "Distinct pairs must produce distinct ids");
    }

    [TestMethod]
    public void StableId_SameInput_IsDeterministic()
    {
        string first  = QuoteIdentity.StableId("Some quote.", "Some Source");
        string second = QuoteIdentity.StableId("Some quote.", "Some Source");

        Assert.AreEqual(first, second);
    }

    /// <summary>
    /// Differing in only one of the two inputs is still a different id — the pair is the key, so
    /// neither half may be ignored.
    /// </summary>
    [TestMethod]
    [DataRow("Quote A", "Source A", "Quote B", "Source B", DisplayName = "both differ")]
    [DataRow("Quote A", "Source A", "Quote B", "Source A", DisplayName = "quote differs only")]
    [DataRow("Quote A", "Source A", "Quote A", "Source B", DisplayName = "source differs only")]
    public void StableId_DifferentInputs_NeverCollide(string quoteA, string sourceA, string quoteB, string sourceB)
        => Assert.AreNotEqual(QuoteIdentity.StableId(quoteA, sourceA), QuoteIdentity.StableId(quoteB, sourceB));

    /// <summary>
    /// Casing and surrounding or repeated whitespace are normalised away, so the same line written
    /// untidily in one source file resolves to the row another file already created.
    /// </summary>
    [TestMethod]
    [DataRow("  FRANKLY,   my dear,   I don't give a damn.  ", "  gone WITH the wind  ", DisplayName = "casing + padding + inner runs")]
    [DataRow("frankly, my dear, i don't give a damn.", "GONE WITH THE WIND", DisplayName = "casing only")]
    [DataRow("Frankly, my dear, I don't give a damn.   ", "Gone with the Wind ", DisplayName = "trailing whitespace only")]
    [DataRow("Frankly,\tmy dear, I don't give a damn.", "Gone\twith the Wind", DisplayName = "tabs as separators")]
    public void StableId_WhitespaceAndCasingDifferences_NormaliseToSameId(string quote, string source)
        => Assert.AreEqual("1aa241c0-9a8f-e348-9d67-fdae91c0f33b", QuoteIdentity.StableId(quote, source));

    /// <summary>
    /// The negative half of the pair above: normalisation collapses whitespace and casing, and stops
    /// there. Removing punctuation or interior characters would silently merge genuinely distinct
    /// quotes, so each of these must remain its own id.
    /// </summary>
    [TestMethod]
    [DataRow("Frankly my dear, I don't give a damn.", DisplayName = "a comma removed")]
    [DataRow("Frankly, my dear, I dont give a damn.", DisplayName = "an apostrophe removed")]
    [DataRow("Frankly, my dear, I don't give a damn", DisplayName = "trailing full stop removed")]
    [DataRow("Frankly, my dear, I don't give a darn.", DisplayName = "one word changed")]
    public void StableId_PunctuationOrWordDifferences_ProduceADifferentId(string alteredQuote)
        => Assert.AreNotEqual(
            "1aa241c0-9a8f-e348-9d67-fdae91c0f33b",
            QuoteIdentity.StableId(alteredQuote, "Gone with the Wind"),
            "Normalisation must not reach past casing and whitespace — these are different quotes");

    [TestMethod]
    [DataRow("  Hello   World  ", "hello world", DisplayName = "pad + inner run")]
    [DataRow("HELLO WORLD", "hello world", DisplayName = "casing")]
    [DataRow("Hello\t\tWorld", "hello world", DisplayName = "tabs")]
    [DataRow("Hello\nWorld", "hello world", DisplayName = "newline")]
    [DataRow("hello world", "hello world", DisplayName = "already normal")]
    public void Normalise_TrimsLowercasesAndCollapsesWhitespace(string input, string expected)
        => Assert.AreEqual(expected, QuoteIdentity.Normalise(input));
}
