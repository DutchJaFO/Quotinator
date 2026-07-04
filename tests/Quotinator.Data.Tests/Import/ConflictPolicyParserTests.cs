using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class ConflictPolicyParserTests
{
    [TestMethod]
    [DataRow(null,           DuplicateResolutionPolicy.NewestWins)]
    [DataRow("",             DuplicateResolutionPolicy.NewestWins)]
    [DataRow("garbage",      DuplicateResolutionPolicy.NewestWins)]
    [DataRow("skip",         DuplicateResolutionPolicy.Skip)]
    [DataRow("SKIP",         DuplicateResolutionPolicy.Skip)]
    [DataRow("newest-wins",  DuplicateResolutionPolicy.NewestWins)]
    [DataRow("merge-ours",   DuplicateResolutionPolicy.MergeOurs)]
    [DataRow("merge-theirs", DuplicateResolutionPolicy.MergeTheirs)]
    [DataRow("review",       DuplicateResolutionPolicy.Review)]
    public void Parse_FallsBackToNewestWinsOnAbsentOrGarbage(string? value, DuplicateResolutionPolicy expected)
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
