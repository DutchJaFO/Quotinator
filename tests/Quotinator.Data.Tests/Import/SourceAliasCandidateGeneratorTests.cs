using System.Linq;
using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

/// <summary>Tests for <see cref="SourceAliasCandidateGenerator"/> (#153 Step 13) — detect-and-suggest only, never auto-writing an alias entry.</summary>
[TestClass]
public class SourceAliasCandidateGeneratorTests
{
    [TestMethod]
    public void Generate_PunctuationOnlyDifference_SurfacesAsCandidate()
    {
        var sources = new[]
        {
            ("id-1", "Airplane!", "movie"),
            ("id-2", "Airplane", "movie"),
        };

        var candidates = SourceAliasCandidateGenerator.Generate(sources, SourceAliasLookup.Empty);

        Assert.HasCount(1, candidates);
        Assert.AreEqual("movie", candidates[0].Type);
    }

    [TestMethod]
    public void Generate_CurlyVsStraightApostrophe_SurfacesAsCandidate()
    {
        var sources = new[]
        {
            ("id-1", "Ocean's Eleven", "movie"),
            ("id-2", "Ocean’s Eleven", "movie"),
        };

        var candidates = SourceAliasCandidateGenerator.Generate(sources, SourceAliasLookup.Empty);

        Assert.HasCount(1, candidates);
    }

    [TestMethod]
    public void Generate_DoubledWhitespace_SurfacesAsCandidate()
    {
        var sources = new[]
        {
            ("id-1", "The  Lord of the Rings", "movie"),
            ("id-2", "The Lord of the Rings", "movie"),
        };

        var candidates = SourceAliasCandidateGenerator.Generate(sources, SourceAliasLookup.Empty);

        Assert.HasCount(1, candidates);
    }

    [TestMethod]
    public void Generate_CaseOnlyDifference_NeverSurfaced()
    {
        // Two rows differing only by case could not both exist as separate Sources under #175's
        // case-insensitive natural-key matching — guard against ever suggesting this class anyway.
        var sources = new[]
        {
            ("id-1", "star wars", "movie"),
            ("id-2", "Star Wars", "movie"),
        };

        var candidates = SourceAliasCandidateGenerator.Generate(sources, SourceAliasLookup.Empty);

        Assert.IsEmpty(candidates);
    }

    [TestMethod]
    public void Generate_SameNormalizedTitleDifferentType_NotGrouped()
    {
        var sources = new[]
        {
            ("id-1", "Airplane!", "movie"),
            ("id-2", "Airplane", "book"),
        };

        var candidates = SourceAliasCandidateGenerator.Generate(sources, SourceAliasLookup.Empty);

        Assert.IsEmpty(candidates);
    }

    [TestMethod]
    public void Generate_NoDuplicates_ReturnsEmpty()
    {
        var sources = new[]
        {
            ("id-1", "Jurassic Park", "movie"),
            ("id-2", "Casablanca", "movie"),
        };

        var candidates = SourceAliasCandidateGenerator.Generate(sources, SourceAliasLookup.Empty);

        Assert.IsEmpty(candidates);
    }

    [TestMethod]
    public void Generate_AlreadyAliasedPair_NotReSuggested()
    {
        var sources = new[]
        {
            ("id-1", "Airplane!", "movie"),
            ("id-2", "Airplane", "movie"),
        };
        var existingAliases = new SourceAliasLookup(
        [
            new SourceAliasRule { Title = "Airplane", Type = "movie", CanonicalTitle = "Airplane!", CanonicalType = "movie" },
        ]);

        var candidates = SourceAliasCandidateGenerator.Generate(sources, existingAliases);

        Assert.IsEmpty(candidates);
    }

    [TestMethod]
    public void Generate_OtherSideAlreadyAliased_NotReSuggested()
    {
        // The alias covers the OTHER title in the pair — still should not re-suggest this pair.
        var sources = new[]
        {
            ("id-1", "Airplane!", "movie"),
            ("id-2", "Airplane", "movie"),
        };
        var existingAliases = new SourceAliasLookup(
        [
            new SourceAliasRule { Title = "Airplane!", Type = "movie", CanonicalTitle = "Airplane!", CanonicalType = "movie" },
        ]);

        var candidates = SourceAliasCandidateGenerator.Generate(sources, existingAliases);

        Assert.IsEmpty(candidates);
    }

    [TestMethod]
    public void Generate_ThreeWayGroup_ProducesEveryPair()
    {
        var sources = new[]
        {
            ("id-1", "Airplane!", "movie"),
            ("id-2", "Airplane", "movie"),
            ("id-3", "Airplane.", "movie"),
        };

        var candidates = SourceAliasCandidateGenerator.Generate(sources, SourceAliasLookup.Empty);

        Assert.HasCount(3, candidates);
    }

    [TestMethod]
    public void Generate_NeverWritesAnAliasEntry()
    {
        // Detect-and-suggest only — the generator has no write path of any kind. Asserted structurally:
        // the return type carries no alias-file model, so there is nothing to persist even by mistake.
        var sources = new[]
        {
            ("id-1", "Airplane!", "movie"),
            ("id-2", "Airplane", "movie"),
        };

        var candidates = SourceAliasCandidateGenerator.Generate(sources, SourceAliasLookup.Empty);

        Assert.HasCount(1, candidates);
        Assert.DoesNotContain(p => p.CanWrite && p.Name is "CanonicalTitle" or "CanonicalType", typeof(SourceAliasCandidate).GetProperties(),
            "SourceAliasCandidate must never carry a canonical title/type to write — that requires human research per docs/workflow/source-verification.md");
    }
}
