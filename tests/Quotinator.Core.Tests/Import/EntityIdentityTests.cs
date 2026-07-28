using Quotinator.Core.Import;

namespace Quotinator.Core.Tests.Import;

[TestClass]
public class EntityIdentityTests
{
    [TestMethod]
    public void SourceId_SameInput_IsDeterministic()
    {
        var first  = EntityIdentity.SourceId("Casablanca", "movie");
        var second = EntityIdentity.SourceId("Casablanca", "movie");

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void SourceId_WhitespaceAndCasingDifferences_NormaliseToSameId()
    {
        var a = EntityIdentity.SourceId("Casablanca", "movie");
        var b = EntityIdentity.SourceId("  CASABLANCA  ", "  Movie  ");

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void SourceId_DifferentInputs_NeverCollide()
    {
        var a = EntityIdentity.SourceId("Casablanca", "movie");
        var b = EntityIdentity.SourceId("Casablanca", "tv");

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void CharacterId_SameInput_IsDeterministic()
    {
        var first  = EntityIdentity.CharacterId("some-source-id", "Rick Blaine", "Movie");
        var second = EntityIdentity.CharacterId("some-source-id", "Rick Blaine", "Movie");

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void CharacterId_DifferentSourceId_NeverCollide()
    {
        var a = EntityIdentity.CharacterId("source-a", "Rick Blaine", "Movie");
        var b = EntityIdentity.CharacterId("source-b", "Rick Blaine", "Movie");

        Assert.AreNotEqual(a, b);
    }

    /// <summary>#174/ADR 013 Decision 5: sourceType stays part of the hash alongside sourceId — defense-in-depth for the Source.Type anchor (ADR 011).</summary>
    [TestMethod]
    public void CharacterId_DifferentSourceType_NeverCollide()
    {
        var a = EntityIdentity.CharacterId("source-a", "Gandalf", "Movie");
        var b = EntityIdentity.CharacterId("source-a", "Gandalf", "Book");

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void PersonId_SameInput_IsDeterministic()
    {
        var first  = EntityIdentity.PersonId("Winston Churchill");
        var second = EntityIdentity.PersonId("Winston Churchill");

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void PersonId_WhitespaceAndCasingDifferences_NormaliseToSameId()
    {
        var a = EntityIdentity.PersonId("Winston Churchill");
        var b = EntityIdentity.PersonId("  winston   churchill  ");

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void SourceId_CharacterId_PersonId_NeverCollideWithEachOtherOrQuoteIdentity()
    {
        var sourceId    = EntityIdentity.SourceId("X", "Y");
        var characterId = EntityIdentity.CharacterId("X", "Y", "Z");
        var personId    = EntityIdentity.PersonId("X");
        var quoteId     = QuoteIdentity.StableId("X", "Y");

        var ids = new[] { sourceId, characterId, personId, quoteId };
        CollectionAssert.AllItemsAreUnique(ids);
    }
}
