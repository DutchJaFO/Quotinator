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
        Assert.AreAllDistinct(ids);
    }

    /// <summary>#375: a season's number identifies it only within its series, so the parent id is part
    /// of the hash. Without this, every series' season 1 derives the same id — the natural key admits
    /// the second row and the primary key rejects it.</summary>
    [TestMethod]
    public void SeasonId_SameNumberUnderDifferentSeries_DiffersById()
    {
        string first  = EntityIdentity.SeasonId(EntityIdentity.SeriesId("Avatar: The Last Airbender"), 1);
        string second = EntityIdentity.SeasonId(EntityIdentity.SeriesId("Mr. Robot"), 1);

        Assert.AreNotEqual(first, second);
    }

    /// <summary>The control for the row above: same parent, same number must be the same id, or a
    /// reimport would create a duplicate season every run.</summary>
    [TestMethod]
    public void SeasonId_SameSeriesAndNumber_IsDeterministic()
    {
        string seriesId = EntityIdentity.SeriesId("Avatar: The Last Airbender");

        Assert.AreEqual(EntityIdentity.SeasonId(seriesId, 1), EntityIdentity.SeasonId(seriesId, 1));
    }

    /// <summary>Different ordinals under one series are different seasons.</summary>
    [TestMethod]
    public void SeasonId_DifferentNumbersUnderOneSeries_DifferById()
    {
        string seriesId = EntityIdentity.SeriesId("Avatar: The Last Airbender");

        Assert.AreNotEqual(EntityIdentity.SeasonId(seriesId, 1), EntityIdentity.SeasonId(seriesId, 2));
    }

    /// <summary>A season id must not collide with any other id space — the type tag is what guarantees it.</summary>
    [TestMethod]
    public void SeasonId_NeverCollidesWithTheOtherIdSpaces()
    {
        string seasonId = EntityIdentity.SeasonId("X", 1);

        string[] ids =
        [
            seasonId,
            EntityIdentity.SourceId("X", "1"),
            EntityIdentity.CharacterId("X", "1", "Z"),
            EntityIdentity.PersonId("X"),
            EntityIdentity.SeriesId("X"),
            EntityIdentity.UniverseId("X"),
            QuoteIdentity.StableId("X", "1"),
        ];
        Assert.AreAllDistinct(ids);
    }
}
