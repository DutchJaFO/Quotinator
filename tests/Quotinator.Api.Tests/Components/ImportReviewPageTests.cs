using Quotinator.Api.Components.Pages;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;

namespace Quotinator.Api.Tests.Components;

/// <summary>
/// Exercises <see cref="ImportReview"/>'s selection rule (#303) — which staged actions belong on the
/// review page. This project has no Blazor component-rendering test infrastructure (no bUnit), so the
/// pure selection method is unit-tested directly rather than via a rendered component, matching
/// <see cref="NotificationTableTests"/>'s own approach.
/// </summary>
[TestClass]
public class ImportReviewPageTests
{
    private static ImportActionEntity Action(ImportActionStatus status, string batchId, string entityType = "Quote") => new()
    {
        BatchId    = batchId,
        EntityType = entityType,
        EntityId   = Guid.NewGuid().ToString("D"),
        ActionType = new SafeValue<ImportActionKind?>(nameof(ImportActionKind.Modify), ImportActionKind.Modify),
        Status     = new SafeValue<ImportActionStatus?>(status.ToString(), status),
    };

    /// <summary>
    /// Everything a human can still act on, from every batch — the page is not scoped to one
    /// notification's file, because an operator resolving a backlog wants the whole backlog.
    /// </summary>
    [TestMethod]
    public void Lists_EveryActiveActionAcrossBatches()
    {
        string batchA = Guid.NewGuid().ToString("D");
        string batchB = Guid.NewGuid().ToString("D");

        List<ImportActionEntity> all =
        [
            Action(ImportActionStatus.Pending, batchA),
            Action(ImportActionStatus.Blocked, batchA),
            Action(ImportActionStatus.Stale,   batchB),
        ];

        List<ImportActionEntity> awaiting = [.. ImportReview.AwaitingReview(all)];

        Assert.HasCount(3, awaiting, "Pending, Blocked and Stale are all awaiting a human decision.");
        Assert.HasCount(2, awaiting.Select(a => a.BatchId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            "Actions from every batch belong here, not just the most recent one.");
    }

    /// <summary>
    /// The negative half: a decision already taken, or a batch already applied or discarded, is not
    /// waiting on anyone. Without this the page would show a permanent, ever-growing backlog of work
    /// that is in fact finished.
    /// </summary>
    [TestMethod]
    public void DecidedRow_LeavesTheActiveList()
    {
        string batchId = Guid.NewGuid().ToString("D");

        List<ImportActionEntity> all =
        [
            Action(ImportActionStatus.Pending,   batchId),
            Action(ImportActionStatus.Decided,   batchId),
            Action(ImportActionStatus.Applied,   batchId),
            Action(ImportActionStatus.Discarded, batchId),
        ];

        List<ImportActionEntity> awaiting = [.. ImportReview.AwaitingReview(all)];

        Assert.HasCount(1, awaiting, "Only the Pending action still needs a decision.");
        Assert.AreEqual(ImportActionStatus.Pending, awaiting[0].Status.Parsed);
    }

    /// <summary>
    /// A row whose stored status cannot be parsed is not silently treated as reviewable. It is data
    /// this application did not write, and inventing a state for it would put phantom work on the page.
    /// </summary>
    [TestMethod]
    public void UnparseableStatus_IsNotTreatedAsAwaitingReview()
    {
        ImportActionEntity unknown = new()
        {
            BatchId    = Guid.NewGuid().ToString("D"),
            EntityType = "Quote",
            EntityId   = Guid.NewGuid().ToString("D"),
            ActionType = new SafeValue<ImportActionKind?>(nameof(ImportActionKind.Modify), ImportActionKind.Modify),
            Status     = new SafeValue<ImportActionStatus?>("NotARealStatus", null),
        };

        Assert.IsEmpty(ImportReview.AwaitingReview([unknown]));
    }
}
