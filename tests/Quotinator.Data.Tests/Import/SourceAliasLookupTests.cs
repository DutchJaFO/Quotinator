using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class SourceAliasLookupTests
{
    [TestMethod]
    public void TryResolve_MatchingTitleAndType_ReturnsTrueWithCanonical()
    {
        var lookup = new SourceAliasLookup([
            new SourceAliasRule { Title = "Avengers : Infinity War", Type = "movie", CanonicalTitle = "Avengers: Infinity War", CanonicalType = "movie" },
        ]);

        var found = lookup.TryResolve("Avengers : Infinity War", "movie", null, out var canonical);

        Assert.IsTrue(found);
        Assert.AreEqual("Avengers: Infinity War", canonical.CanonicalTitle);
        Assert.AreEqual("movie", canonical.CanonicalType);
    }

    [TestMethod]
    public void TryResolve_TitleAndTypeDifferOnlyByCase_StillMatches()
    {
        var lookup = new SourceAliasLookup([
            new SourceAliasRule { Title = "Zootopia", Type = "anime", CanonicalTitle = "Zootopia", CanonicalType = "movie" },
        ]);

        var found = lookup.TryResolve("ZOOTOPIA", "ANIME", null, out var canonical);

        Assert.IsTrue(found, "Title/type matching must be case-insensitive, per this project's value-comparison convention");
        Assert.AreEqual("movie", canonical.CanonicalType);
    }

    [TestMethod]
    public void TryResolve_NoMatchingAlias_ReturnsFalse()
    {
        var lookup = new SourceAliasLookup([
            new SourceAliasRule { Title = "Matrix", Type = "movie", CanonicalTitle = "The Matrix", CanonicalType = "movie" },
        ]);

        Assert.IsFalse(lookup.TryResolve("Matrix", "tv", null, out _), "An alias for a different type must not match");
        Assert.IsFalse(lookup.TryResolve("The Matrix", "movie", null, out _), "The already-canonical title must not itself match an alias entry");
    }

    [TestMethod]
    public void Empty_TryResolve_AlwaysReturnsFalse()
        => Assert.IsFalse(SourceAliasLookup.Empty.TryResolve("Matrix", "movie", null, out _));

    [TestMethod]
    public void TryResolve_TwoRawVariantsAliasToSameCanonical_BothResolve()
    {
        var lookup = new SourceAliasLookup([
            new SourceAliasRule { Title = "Adonis, Creed II", Type = "movie", CanonicalTitle = "Creed II", CanonicalType = "movie" },
            new SourceAliasRule { Title = "Creed 2", Type = "movie", CanonicalTitle = "Creed II", CanonicalType = "movie" },
        ]);

        Assert.IsTrue(lookup.TryResolve("Adonis, Creed II", "movie", null, out var first));
        Assert.AreEqual("Creed II", first.CanonicalTitle);
        Assert.IsTrue(lookup.TryResolve("Creed 2", "movie", null, out var second));
        Assert.AreEqual("Creed II", second.CanonicalTitle);
    }

    // ── #374, step 8: date joins the alias key ──────────────────────────────────

    /// <summary>
    /// #374, verification row 28 — a wrong title claimed under two different dates (e.g. a typo on one
    /// entry, a genuinely distinct work on the other) needs an alias that only fires for the specific
    /// raw date it corrects, not both.
    /// </summary>
    [TestMethod]
    public void TryResolve_AliasWithDate_TargetsTheMatchingSourceOnly()
    {
        var lookup = new SourceAliasLookup([
            new SourceAliasRule { Title = "Back to the future", Type = "movie", Date = "1958", CanonicalTitle = "Back to the Future", CanonicalType = "movie", CanonicalDate = "1985" },
        ]);

        Assert.IsTrue(lookup.TryResolve("Back to the future", "movie", "1958", out var corrected), "The exact dated raw entry the alias targets must resolve");
        Assert.AreEqual("Back to the Future", corrected.CanonicalTitle);
        Assert.AreEqual("1985", corrected.CanonicalDate);

        Assert.IsFalse(lookup.TryResolve("Back to the future", "movie", "1985", out _), "A different date on the same raw title must not match a dated alias scoped to a different date");
    }

    /// <summary>
    /// #374, verification row 29 — the three alias files shipped before this field existed carry no
    /// <c>date</c>/<c>canonicalDate</c> at all. A date-less alias must keep matching every date of its
    /// raw title, exactly as it did before #374, so those files load and apply unchanged.
    /// </summary>
    [TestMethod]
    public void TryResolve_AliasWithoutDate_StillApplies()
    {
        var lookup = new SourceAliasLookup([
            new SourceAliasRule { Title = "Marvel's The Avengers", Type = "movie", CanonicalTitle = "The Avengers", CanonicalType = "movie" },
        ]);

        Assert.IsTrue(lookup.TryResolve("Marvel's The Avengers", "movie", "2012", out var withDate), "A date-less alias must still match when the incoming quote carries a date");
        Assert.AreEqual("The Avengers", withDate.CanonicalTitle);
        Assert.IsNull(withDate.CanonicalDate, "A date-less alias never corrects the date");

        Assert.IsTrue(lookup.TryResolve("Marvel's The Avengers", "movie", null, out var withoutDate), "A date-less alias must still match when the incoming quote itself has no date, exactly as before #374");
        Assert.AreEqual("The Avengers", withoutDate.CanonicalTitle);
    }

    /// <summary>A dated alias for the raw title must not shadow the date-less fallback for a different, unlisted date on the same raw title.</summary>
    [TestMethod]
    public void TryResolve_DatedAliasForOneDate_DatelessAliasStillCoversOtherDates()
    {
        var lookup = new SourceAliasLookup([
            new SourceAliasRule { Title = "Zootopia", Type = "anime", CanonicalTitle = "Zootopia", CanonicalType = "movie" },
            new SourceAliasRule { Title = "Zootopia", Type = "anime", Date = "1958", CanonicalTitle = "Zootopia", CanonicalType = "movie", CanonicalDate = "2016" },
        ]);

        Assert.IsTrue(lookup.TryResolve("Zootopia", "anime", "1958", out var dated), "The dated alias takes priority over the date-less one for its own specific date");
        Assert.AreEqual("2016", dated.CanonicalDate);

        Assert.IsTrue(lookup.TryResolve("Zootopia", "anime", "1999", out var fallback), "A date not covered by the dated alias must still fall back to the date-less one");
        Assert.IsNull(fallback.CanonicalDate);
    }
}
