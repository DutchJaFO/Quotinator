using Quotinator.Api.Components.Pages;
using Quotinator.Core.Models;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;

namespace Quotinator.Api.Tests.Components;

/// <summary>
/// Exercises <see cref="ImportReview"/>'s selection and decision rules (#303) — which staged actions
/// belong on the review page, and what a whole-action decision actually decides. This project has no
/// Blazor component-rendering test infrastructure (no bUnit), so these pure methods are unit-tested
/// directly rather than via a rendered component, matching <see cref="NotificationTableTests"/>.
/// </summary>
[TestClass]
public class ImportReviewPageTests
{
    private static ImportActionSummaryResponse Summary(
        ImportActionStatus status,
        string batchId,
        string entityType = "Quote",
        params string[] ambiguousFields) => new()
        {
            Id              = Guid.NewGuid(),
            BatchId         = batchId,
            ActionType      = nameof(ImportActionKind.Modify),
            EntityType      = entityType,
            EntityId        = Guid.NewGuid().ToString("D"),
            Status          = status.ToString(),
            DetectedAt      = DateTime.UtcNow,
            IncomingFields  = new Dictionary<string, object?>(),
            AmbiguousFields = ambiguousFields,
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

        List<ImportActionSummaryResponse> all =
        [
            Summary(ImportActionStatus.Pending, batchA),
            Summary(ImportActionStatus.Blocked, batchA),
            Summary(ImportActionStatus.Stale,   batchB),
        ];

        List<ImportActionSummaryResponse> awaiting = [.. ImportReview.AwaitingReview(all)];

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

        List<ImportActionSummaryResponse> all =
        [
            Summary(ImportActionStatus.Pending,   batchId),
            Summary(ImportActionStatus.Decided,   batchId),
            Summary(ImportActionStatus.Applied,   batchId),
            Summary(ImportActionStatus.Discarded, batchId),
        ];

        List<ImportActionSummaryResponse> awaiting = [.. ImportReview.AwaitingReview(all)];

        Assert.HasCount(1, awaiting, "Only the Pending action still needs a decision.");
        Assert.AreEqual(nameof(ImportActionStatus.Pending), awaiting[0].Status);
    }

    /// <summary>
    /// A row whose stored status cannot be parsed is not silently treated as reviewable. It is data
    /// this application did not write, and inventing a state for it would put phantom work on the page.
    /// </summary>
    [TestMethod]
    public void UnparseableStatus_IsNotTreatedAsAwaitingReview()
    {
        ImportActionSummaryResponse unknown = Summary(ImportActionStatus.Pending, Guid.NewGuid().ToString("D"));
        ImportActionSummaryResponse broken = new()
        {
            Id             = unknown.Id,
            BatchId        = unknown.BatchId,
            ActionType     = unknown.ActionType,
            EntityType     = unknown.EntityType,
            EntityId       = unknown.EntityId,
            Status         = "NotARealStatus",
            DetectedAt     = unknown.DetectedAt,
            IncomingFields = unknown.IncomingFields,
        };

        Assert.IsEmpty(ImportReview.AwaitingReview([broken]));
    }

    /// <summary>
    /// The whole-action decision resolves exactly the conflicted fields, and nothing else — the
    /// degenerate case of git's own <c>--ours</c>/<c>--theirs</c>, which resolve the conflicted hunks
    /// and leave the rest of the merge alone.
    /// <para>
    /// Deciding every decidable field instead would silently overwrite fields nobody was asked about,
    /// including nulling one the incoming file simply does not carry.
    /// </para>
    /// </summary>
    [TestMethod]
    public void Decision_CoversOnlyTheAmbiguousFields()
    {
        ImportActionSummaryResponse conflicted = Summary(
            ImportActionStatus.Pending, Guid.NewGuid().ToString("D"), "Quote", "quoteText");

        List<ImportActionFieldRowDto> rows = [.. ImportReview.DecisionRows(conflicted, FieldResolutionChoice.Keep)];

        Assert.HasCount(1, rows, "One conflicted field is one decision — the other decidable fields were never in question.");
        Assert.AreEqual("quoteText", rows[0].Field);
        Assert.AreEqual(FieldResolutionChoice.Keep, rows[0].Decision);
        Assert.AreEqual(conflicted.Id, rows[0].ActionId);
    }

    /// <summary>Taking the incoming side is the same shape with the opposite choice — git's <c>--theirs</c>.</summary>
    [TestMethod]
    public void Decision_TakingIncoming_SetsTheOppositeChoice()
    {
        ImportActionSummaryResponse conflicted = Summary(
            ImportActionStatus.Pending, Guid.NewGuid().ToString("D"), "Quote", "quoteText", "source");

        List<ImportActionFieldRowDto> rows = [.. ImportReview.DecisionRows(conflicted, FieldResolutionChoice.Replace)];

        Assert.HasCount(2, rows);
        Assert.IsTrue(rows.All(r => r.Decision == FieldResolutionChoice.Replace));
    }

    /// <summary>
    /// A Blocked action has no ambiguous fields — it is held because it would touch a protected field,
    /// not because two values disagree. It therefore has nothing for a whole-action decision to resolve,
    /// and must not produce an empty decision that silently reports success.
    /// </summary>
    [TestMethod]
    public void Decision_ActionWithNoAmbiguousFields_ProducesNoRows()
    {
        ImportActionSummaryResponse blocked = Summary(ImportActionStatus.Blocked, Guid.NewGuid().ToString("D"));

        Assert.IsEmpty(ImportReview.DecisionRows(blocked, FieldResolutionChoice.Keep));
    }
}
