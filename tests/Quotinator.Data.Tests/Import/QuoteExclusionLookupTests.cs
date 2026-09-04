using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class QuoteExclusionLookupTests
{
    [TestMethod]
    public void Contains_MatchingId_ReturnsTrue()
    {
        var lookup = new QuoteExclusionLookup([
            new QuoteExclusionRule { Id = "7e53658c-0c3a-6546-8c12-c5e4af23c9f8", Reason = "Duplicate entry" },
        ]);

        Assert.IsTrue(lookup.Contains("7e53658c-0c3a-6546-8c12-c5e4af23c9f8"));
    }

    [TestMethod]
    public void Contains_IdDiffersOnlyByCase_StillMatches()
    {
        var lookup = new QuoteExclusionLookup([
            new QuoteExclusionRule { Id = "7E53658C-0C3A-6546-8C12-C5E4AF23C9F8", Reason = "Duplicate entry" },
        ]);

        Assert.IsTrue(lookup.Contains("7e53658c-0c3a-6546-8c12-c5e4af23c9f8"), "Id matching must be case-insensitive, per this project's id-comparison convention");
    }

    [TestMethod]
    public void Contains_NoMatchingExclusion_ReturnsFalse()
    {
        var lookup = new QuoteExclusionLookup([
            new QuoteExclusionRule { Id = "7e53658c-0c3a-6546-8c12-c5e4af23c9f8", Reason = "Duplicate entry" },
        ]);

        Assert.IsFalse(lookup.Contains("e97a8197-657f-ce43-8032-4edd8e3549a3"));
    }

    [TestMethod]
    public void Empty_Contains_AlwaysReturnsFalse()
        => Assert.IsFalse(QuoteExclusionLookup.Empty.Contains("7e53658c-0c3a-6546-8c12-c5e4af23c9f8"));
}
