using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class ConflictRuleLookupTests
{
    [TestMethod]
    public void TryResolve_MatchingQuoteIdAndField_ReturnsTrueWithResolution()
    {
        var lookup = new ConflictRuleLookup([
            new ConflictResolutionRule { QuoteId = "abc123", Field = "date", Resolution = FieldResolutionChoice.Keep },
        ]);

        var found = lookup.TryResolve("abc123", "date", out var resolution);

        Assert.IsTrue(found);
        Assert.AreEqual(FieldResolutionChoice.Keep, resolution);
    }

    [TestMethod]
    public void TryResolve_QuoteIdDiffersOnlyByCase_StillMatches()
    {
        var lookup = new ConflictRuleLookup([
            new ConflictResolutionRule { QuoteId = "ABC123", Field = "date", Resolution = FieldResolutionChoice.Keep },
        ]);

        var found = lookup.TryResolve("abc123", "date", out _);

        Assert.IsTrue(found, "Quote id matching must be case-insensitive, per this project's id-comparison convention");
    }

    [TestMethod]
    public void TryResolve_NoMatchingRule_ReturnsFalse()
    {
        var lookup = new ConflictRuleLookup([
            new ConflictResolutionRule { QuoteId = "abc123", Field = "date", Resolution = FieldResolutionChoice.Keep },
        ]);

        Assert.IsFalse(lookup.TryResolve("abc123", "type", out _), "A rule for a different field must not match");
        Assert.IsFalse(lookup.TryResolve("xyz789", "date", out _), "A rule for a different quote id must not match");
    }

    [TestMethod]
    public void Empty_TryResolve_AlwaysReturnsFalse()
        => Assert.IsFalse(ConflictRuleLookup.Empty.TryResolve("abc123", "date", out _));
}
