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

        var found = lookup.TryResolve("Avengers : Infinity War", "movie", out var canonical);

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

        var found = lookup.TryResolve("ZOOTOPIA", "ANIME", out var canonical);

        Assert.IsTrue(found, "Title/type matching must be case-insensitive, per this project's value-comparison convention");
        Assert.AreEqual("movie", canonical.CanonicalType);
    }

    [TestMethod]
    public void TryResolve_NoMatchingAlias_ReturnsFalse()
    {
        var lookup = new SourceAliasLookup([
            new SourceAliasRule { Title = "Matrix", Type = "movie", CanonicalTitle = "The Matrix", CanonicalType = "movie" },
        ]);

        Assert.IsFalse(lookup.TryResolve("Matrix", "tv", out _), "An alias for a different type must not match");
        Assert.IsFalse(lookup.TryResolve("The Matrix", "movie", out _), "The already-canonical title must not itself match an alias entry");
    }

    [TestMethod]
    public void Empty_TryResolve_AlwaysReturnsFalse()
        => Assert.IsFalse(SourceAliasLookup.Empty.TryResolve("Matrix", "movie", out _));

    [TestMethod]
    public void TryResolve_TwoRawVariantsAliasToSameCanonical_BothResolve()
    {
        var lookup = new SourceAliasLookup([
            new SourceAliasRule { Title = "Adonis, Creed II", Type = "movie", CanonicalTitle = "Creed II", CanonicalType = "movie" },
            new SourceAliasRule { Title = "Creed 2", Type = "movie", CanonicalTitle = "Creed II", CanonicalType = "movie" },
        ]);

        Assert.IsTrue(lookup.TryResolve("Adonis, Creed II", "movie", out var first));
        Assert.AreEqual("Creed II", first.CanonicalTitle);
        Assert.IsTrue(lookup.TryResolve("Creed 2", "movie", out var second));
        Assert.AreEqual("Creed II", second.CanonicalTitle);
    }
}
