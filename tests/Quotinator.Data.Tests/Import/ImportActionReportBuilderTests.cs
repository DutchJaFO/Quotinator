using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Models;

namespace Quotinator.Data.Tests.Import;

/// <summary>Tests for <see cref="ImportActionReportBuilder"/> (#221).</summary>
[TestClass]
public class ImportActionReportBuilderTests
{
    private static ImportActionEntity Action(string entityType, ImportActionKind kind, ImportActionStatus status) => new()
    {
        EntityType = entityType,
        ActionType = new SafeValue<ImportActionKind?>(kind.ToString(), kind),
        Status     = new SafeValue<ImportActionStatus?>(status.ToString(), status),
    };

    [TestMethod]
    public void Build_NoActions_ReturnsEmptyEntityTypes()
    {
        var report = ImportActionReportBuilder.Build("file.json", []);

        Assert.AreEqual("file.json", report.FileName);
        Assert.IsEmpty(report.EntityTypes);
    }

    [TestMethod]
    public void Build_AddDecided_CountsAsNew()
    {
        var report = ImportActionReportBuilder.Build("file.json", [Action("Quote", ImportActionKind.Add, ImportActionStatus.Decided)]);

        var counts = report.EntityTypes["Quote"];
        Assert.AreEqual(1, counts.New);
        Assert.AreEqual(0, counts.Modified);
        Assert.AreEqual(0, counts.Blocked);
        Assert.AreEqual(0, counts.Discarded);
        Assert.AreEqual(0, counts.Pending);
        Assert.AreEqual(0, counts.Stale);
    }

    [TestMethod]
    public void Build_AddApplied_AlsoCountsAsNew()
    {
        var report = ImportActionReportBuilder.Build("file.json", [Action("Quote", ImportActionKind.Add, ImportActionStatus.Applied)]);

        Assert.AreEqual(1, report.EntityTypes["Quote"].New);
    }

    [TestMethod]
    public void Build_ModifyApplied_CountsAsModified()
    {
        var report = ImportActionReportBuilder.Build("file.json", [Action("Quote", ImportActionKind.Modify, ImportActionStatus.Applied)]);

        Assert.AreEqual(1, report.EntityTypes["Quote"].Modified);
        Assert.AreEqual(0, report.EntityTypes["Quote"].New);
    }

    [TestMethod]
    public void Build_ModifyDecided_AlsoCountsAsModified()
    {
        var report = ImportActionReportBuilder.Build("file.json", [Action("Quote", ImportActionKind.Modify, ImportActionStatus.Decided)]);

        Assert.AreEqual(1, report.EntityTypes["Quote"].Modified);
    }

    [TestMethod]
    public void Build_AddStale_CountsAsStale_NotNew()
    {
        // #153: a stale source-alias substitution can leave a fresh Add action Stale.
        var report = ImportActionReportBuilder.Build("file.json", [Action("Quote", ImportActionKind.Add, ImportActionStatus.Stale)]);

        Assert.AreEqual(1, report.EntityTypes["Quote"].Stale);
        Assert.AreEqual(0, report.EntityTypes["Quote"].New);
    }

    [TestMethod]
    public void Build_ModifyBlocked_CountsAsBlocked()
    {
        var report = ImportActionReportBuilder.Build("file.json", [Action("Quote", ImportActionKind.Modify, ImportActionStatus.Blocked)]);

        Assert.AreEqual(1, report.EntityTypes["Quote"].Blocked);
    }

    [TestMethod]
    public void Build_ModifyDiscarded_CountsAsDiscarded()
    {
        var report = ImportActionReportBuilder.Build("file.json", [Action("Quote", ImportActionKind.Modify, ImportActionStatus.Discarded)]);

        Assert.AreEqual(1, report.EntityTypes["Quote"].Discarded);
    }

    [TestMethod]
    public void Build_ModifyPending_CountsAsPending()
    {
        var report = ImportActionReportBuilder.Build("file.json", [Action("Quote", ImportActionKind.Modify, ImportActionStatus.Pending)]);

        Assert.AreEqual(1, report.EntityTypes["Quote"].Pending);
    }

    [TestMethod]
    public void Build_ModifyStale_CountsAsStale()
    {
        var report = ImportActionReportBuilder.Build("file.json", [Action("Quote", ImportActionKind.Modify, ImportActionStatus.Stale)]);

        Assert.AreEqual(1, report.EntityTypes["Quote"].Stale);
    }

    // ── #373: unchanged is its own outcome, and every incoming item is accounted for ───────────────

    /// <summary>An unchanged action is neither new nor modified — it is its own bucket.</summary>
    [TestMethod]
    public void Build_UnchangedApplied_CountsAsUnchanged()
    {
        var report = ImportActionReportBuilder.Build("file.json", [Action("Quote", ImportActionKind.Unchanged, ImportActionStatus.Applied)]);

        var counts = report.EntityTypes["Quote"];
        Assert.AreEqual(1, counts.Unchanged);
        Assert.AreEqual(0, counts.New);
        Assert.AreEqual(0, counts.Modified, "Reporting it as modified is the defect this issue exists for.");
    }

    /// <summary>
    /// #373: `Incoming` is every action for the entity type, so it must equal the sum of the outcome
    /// buckets. That identity is the point — the builder has two `_ => counts` fall-through arms that
    /// discard an action matching neither, and a total that no longer adds up is the only thing that
    /// makes such a row observable.
    /// </summary>
    [TestMethod]
    public void Incoming_EqualsTheSumOfEveryOutcome()
    {
        var report = ImportActionReportBuilder.Build("file.json",
        [
            Action("Quote", ImportActionKind.Add,       ImportActionStatus.Decided),
            Action("Quote", ImportActionKind.Modify,    ImportActionStatus.Applied),
            Action("Quote", ImportActionKind.Unchanged, ImportActionStatus.Applied),
            Action("Quote", ImportActionKind.Modify,    ImportActionStatus.Pending),
            Action("Quote", ImportActionKind.Modify,    ImportActionStatus.Blocked),
            Action("Quote", ImportActionKind.Modify,    ImportActionStatus.Discarded),
            Action("Quote", ImportActionKind.Add,       ImportActionStatus.Stale),
        ]);

        var counts = report.EntityTypes["Quote"];
        Assert.AreEqual(7, counts.Incoming, "Seven actions arrived for this entity type.");
        Assert.AreEqual(
            counts.New + counts.Modified + counts.Unchanged + counts.Blocked + counts.Discarded + counts.Pending + counts.Stale,
            counts.Incoming,
            "Every incoming action lands in exactly one bucket. A shortfall means the builder silently dropped one.");
    }

    /// <summary>
    /// The row the identity above exists to catch: an action whose status matches no arm is discarded
    /// today rather than counted, so nothing reports that it arrived at all.
    /// </summary>
    [TestMethod]
    public void Build_ActionMatchingNoOutcomeArm_IsStillCountedAsIncoming()
    {
        // A Decided action with no parsable kind reaches the inner switch and falls through its
        // `_ => counts` arm — the shape a future ImportActionKind member would have before the builder
        // learned about it.
        ImportActionEntity unmatched = new()
        {
            EntityType = "Quote",
            ActionType = new SafeValue<ImportActionKind?>("SomethingTheBuilderDoesNotKnow", null),
            Status     = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
        };

        var counts = ImportActionReportBuilder.Build("file.json", [unmatched]).EntityTypes["Quote"];

        Assert.AreEqual(1, counts.Incoming,
            "It arrived. Falling through every outcome arm is a reason to report it, not to forget it.");
    }

    [TestMethod]
    public void Build_MultipleEntityTypes_EachGetsOwnIndependentCounts()
    {
        var report = ImportActionReportBuilder.Build("file.json",
        [
            Action("Quote", ImportActionKind.Add, ImportActionStatus.Decided),
            Action("Quote", ImportActionKind.Add, ImportActionStatus.Decided),
            Action("Source", ImportActionKind.Modify, ImportActionStatus.Applied),
        ]);

        Assert.AreEqual(2, report.EntityTypes["Quote"].New);
        Assert.AreEqual(1, report.EntityTypes["Source"].Modified);
        Assert.AreEqual(0, report.EntityTypes["Source"].New);
    }

    [TestMethod]
    public void Build_MultipleActionsSameBucket_CountsAccumulate()
    {
        var report = ImportActionReportBuilder.Build("file.json",
        [
            Action("Quote", ImportActionKind.Modify, ImportActionStatus.Pending),
            Action("Quote", ImportActionKind.Modify, ImportActionStatus.Pending),
            Action("Quote", ImportActionKind.Modify, ImportActionStatus.Pending),
        ]);

        Assert.AreEqual(3, report.EntityTypes["Quote"].Pending);
    }

    [TestMethod]
    public void Build_EntityTypeWithNoActions_IsOmittedFromResult()
    {
        var report = ImportActionReportBuilder.Build("file.json", [Action("Quote", ImportActionKind.Add, ImportActionStatus.Decided)]);

        Assert.IsFalse(report.EntityTypes.ContainsKey("Source"));
    }
}
