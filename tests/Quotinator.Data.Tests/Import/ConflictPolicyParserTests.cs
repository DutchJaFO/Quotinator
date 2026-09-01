using Quotinator.Data.Enums;
using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class ConflictPolicyParserTests
{
    /// <summary>
    /// #303: an absent or unreadable value falls back to <c>Review</c>, not <c>NewestWins</c>. This is
    /// the only default a running instance reaches, and the previous one overwrote stored quotes with
    /// an unmanifested import without saying so.
    /// </summary>
    [TestMethod]
    [DataRow(null,           DuplicateResolutionPolicy.Review)]
    [DataRow("",             DuplicateResolutionPolicy.Review)]
    [DataRow("garbage",      DuplicateResolutionPolicy.Review)]
    [DataRow("skip",         DuplicateResolutionPolicy.Skip)]
    [DataRow("SKIP",         DuplicateResolutionPolicy.Skip)]
    [DataRow("newest-wins",  DuplicateResolutionPolicy.NewestWins)]
    [DataRow("merge-ours",   DuplicateResolutionPolicy.MergeOurs)]
    [DataRow("merge-theirs", DuplicateResolutionPolicy.MergeTheirs)]
    [DataRow("review",       DuplicateResolutionPolicy.Review)]
    public void Parse_FallsBackToReviewOnAbsentOrGarbage(string? value, DuplicateResolutionPolicy expected)
    {
        Assert.AreEqual(expected, ConflictPolicyParser.Parse(value));
    }

    [TestMethod]
    [DataRow(null,           null)]
    [DataRow("",             null)]
    [DataRow("garbage",      null)]
    [DataRow("skip",         DuplicateResolutionPolicy.Skip)]
    [DataRow("newest-wins",  DuplicateResolutionPolicy.NewestWins)]
    [DataRow("merge-ours",   DuplicateResolutionPolicy.MergeOurs)]
    [DataRow("merge-theirs", DuplicateResolutionPolicy.MergeTheirs)]
    [DataRow("review",       DuplicateResolutionPolicy.Review)]
    public void ParseNullable_ReturnsNullOnAbsentOrGarbage(string? value, DuplicateResolutionPolicy? expected)
    {
        Assert.AreEqual(expected, ConflictPolicyParser.ParseNullable(value));
    }
}
