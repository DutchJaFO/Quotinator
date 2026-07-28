using Quotinator.Data.Entities;
using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

/// <summary>Exercises <see cref="CompletenessGuard"/> — pure functions, no database needed.</summary>
[TestClass]
public class CompletenessGuardTests
{
    // ── ShouldBlock ───────────────────────────────────────────────────────────

    [TestMethod]
    public void ShouldBlock_StatusComplete_ChangedFieldsPresent_ReturnsTrue()
        => Assert.IsTrue(CompletenessGuard.ShouldBlock(CompletenessStatus.Complete, new HashSet<string> { "title" }));

    [TestMethod]
    public void ShouldBlock_StatusComplete_NoChangedFields_ReturnsFalse()
        => Assert.IsFalse(CompletenessGuard.ShouldBlock(CompletenessStatus.Complete, new HashSet<string>()));

    [TestMethod]
    public void ShouldBlock_StatusNeedsReview_ChangedFieldsPresent_ReturnsFalse()
        => Assert.IsFalse(CompletenessGuard.ShouldBlock(CompletenessStatus.NeedsReview, new HashSet<string> { "title" }));

    [TestMethod]
    public void ShouldBlock_StatusIncomplete_ChangedFieldsPresent_ReturnsFalse()
        => Assert.IsFalse(CompletenessGuard.ShouldBlock(CompletenessStatus.Incomplete, new HashSet<string> { "title" }));

    // ── ComputeNextStatus ─────────────────────────────────────────────────────

    [TestMethod]
    public void ComputeNextStatus_IncompleteWithEmptyNoValueKnown_TransitionsToNeedsReview()
        => Assert.AreEqual(CompletenessStatus.NeedsReview, CompletenessGuard.ComputeNextStatus(CompletenessStatus.Incomplete, []));

    [TestMethod]
    public void ComputeNextStatus_FreshRowFullySpecifiedAtCreation_AlsoTransitionsToNeedsReview()
        => Assert.AreEqual(
            CompletenessStatus.NeedsReview,
            CompletenessGuard.ComputeNextStatus(CompletenessStatus.Incomplete, []),
            "A row created with every field already known has just as much reason to surface for review as one that reached that state later — empty NoValueKnown means the same thing either way.");

    [TestMethod]
    public void ComputeNextStatus_IncompleteWithNonEmptyNoValueKnown_StaysIncomplete()
        => Assert.AreEqual(CompletenessStatus.Incomplete, CompletenessGuard.ComputeNextStatus(CompletenessStatus.Incomplete, ["date"]));

    [TestMethod]
    public void ComputeNextStatus_NeedsReview_NeverChanges()
    {
        Assert.AreEqual(CompletenessStatus.NeedsReview, CompletenessGuard.ComputeNextStatus(CompletenessStatus.NeedsReview, []));
        Assert.AreEqual(CompletenessStatus.NeedsReview, CompletenessGuard.ComputeNextStatus(CompletenessStatus.NeedsReview, ["date"]));
    }

    [TestMethod]
    public void ComputeNextStatus_Complete_NeverDemoted()
    {
        Assert.AreEqual(CompletenessStatus.Complete, CompletenessGuard.ComputeNextStatus(CompletenessStatus.Complete, []));
        Assert.AreEqual(CompletenessStatus.Complete, CompletenessGuard.ComputeNextStatus(CompletenessStatus.Complete, ["date"]));
    }
}
