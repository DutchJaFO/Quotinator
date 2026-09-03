using Quotinator.Core.Import;

namespace Quotinator.Core.Tests.Import;

[TestClass]
public class EntityIdentityTests
{
    [TestMethod]
    public void SourceId_SameInput_IsDeterministic()
    {
        string first  = EntityIdentity.SourceId("Casablanca", "movie");
        string second = EntityIdentity.SourceId("Casablanca", "movie");

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void SourceId_WhitespaceAndCasingDifferences_NormaliseToSameId()
    {
        string a = EntityIdentity.SourceId("Casablanca", "movie");
        string b = EntityIdentity.SourceId("  CASABLANCA  ", "  Movie  ");

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void SourceId_DifferentInputs_NeverCollide()
    {
        string a = EntityIdentity.SourceId("Casablanca", "movie");
        string b = EntityIdentity.SourceId("Casablanca", "tv");

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void CharacterId_SameInput_IsDeterministic()
    {
        string first  = EntityIdentity.CharacterId("some-source-id", "Rick Blaine", "Movie");
        string second = EntityIdentity.CharacterId("some-source-id", "Rick Blaine", "Movie");

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void CharacterId_DifferentSourceId_NeverCollide()
    {
        string a = EntityIdentity.CharacterId("source-a", "Rick Blaine", "Movie");
        string b = EntityIdentity.CharacterId("source-b", "Rick Blaine", "Movie");

        Assert.AreNotEqual(a, b);
    }

    /// <summary>#174/ADR 013 Decision 5: sourceType stays part of the hash alongside sourceId — defense-in-depth for the Source.Type anchor (ADR 011).</summary>
    [TestMethod]
    public void CharacterId_DifferentSourceType_NeverCollide()
    {
        string a = EntityIdentity.CharacterId("source-a", "Gandalf", "Movie");
        string b = EntityIdentity.CharacterId("source-a", "Gandalf", "Book");

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void PersonId_SameInput_IsDeterministic()
    {
        string first  = EntityIdentity.PersonId("Winston Churchill");
        string second = EntityIdentity.PersonId("Winston Churchill");

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void PersonId_WhitespaceAndCasingDifferences_NormaliseToSameId()
    {
        string a = EntityIdentity.PersonId("Winston Churchill");
        string b = EntityIdentity.PersonId("  winston   churchill  ");

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void SourceId_CharacterId_PersonId_NeverCollideWithEachOtherOrQuoteIdentity()
    {
        string sourceId    = EntityIdentity.SourceId("X", "Y");
        string characterId = EntityIdentity.CharacterId("X", "Y", "Z");
        string personId    = EntityIdentity.PersonId("X");
        string quoteId     = QuoteIdentity.StableId("X", "Y");

        string[] ids = [sourceId, characterId, personId, quoteId];
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

    /// <summary>
    /// #375: a differently-cased parent id must derive the same season id. The fixture deliberately
    /// contains hex letters (a–f) — a digits-only GUID is identical in either case and would make this
    /// assertion vacuous.
    /// </summary>
    [TestMethod]
    public void SeasonId_SeriesIdCasingDiffers_ProducesSameId()
    {
        const string lower = "9a02c1dc-8a7f-1f4e-9b90-3229f4c2a361";
        string upper = lower.ToUpperInvariant();

        Assert.AreNotEqual(lower, upper, StringComparer.Ordinal,
            "Fixture guard: the id must contain hex letters, or this test proves nothing.");
        Assert.AreEqual(EntityIdentity.SeasonId(lower, 1), EntityIdentity.SeasonId(upper, 1));
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
