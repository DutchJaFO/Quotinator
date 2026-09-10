using Quotinator.Api.Components.Pages;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Models;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Notifications;

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

    // Hex letters in both, so a case-insensitive comparison is actually exercised rather than passing
    // because an all-digit id has no casing to differ in.
    private const string LiveBatch  = "7f00000a-0000-4000-8000-00000000000b";
    private const string GoneBatch  = "7f00000c-0000-4000-8000-00000000000d";
    private const string Unresolved = "Import batch no longer exists";

    private static readonly Dictionary<string, string> NoLiveBatches   = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> NoRecordedNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A pending-review alert as the notification history stores it, naming <paramref name="batchId"/>'s file.</summary>
    private static NotificationEntity ReviewAlert(string batchId, string fileName, bool isDismissed) => new()
    {
        Body         = $"Your imported file {fileName} was reseeded, but 1 changes need your decision before they can be applied.",
        IsDismissed  = isDismissed,
        MetadataKind = new SafeValue<NotificationMetadataKind?>(nameof(NotificationMetadataKind.ImportReviewPending), NotificationMetadataKind.ImportReviewPending),
        Metadata     = NotificationMetadataKinds.Serialize(new ImportReviewPendingMetadataDto
        {
            FileName     = fileName,
            Origin       = FileResourceOrigin.User,
            BatchId      = batchId,
            ReleaseState = NotificationReleaseState.NotApplicable,
        }),
    };

    /// <summary>
    /// #303, from T1: the page names the file a conflict came from, not the batch id. A GUID is correct
    /// and useless — the operator needs to know which file to go and fix.
    /// <para>
    /// #369, and a control: the live batch's own name still wins over the copy its alert recorded. It
    /// passed before #369 and must keep passing.
    /// </para>
    /// </summary>
    [TestMethod]
    public void FileNameFor_KnownBatch_ReportsTheFileItWasImportedFrom()
    {
        Dictionary<string, string> live     = new(StringComparer.OrdinalIgnoreCase) { [LiveBatch] = "conflicting.json" };
        Dictionary<string, string> recorded = new(StringComparer.OrdinalIgnoreCase) { [LiveBatch] = "renamed-since.json" };

        Assert.AreEqual("conflicting.json", ImportReview.FileNameFor(live, recorded, LiveBatch, Unresolved));
    }

    /// <summary>
    /// #369: a batch that no longer exists is named from the alert raised for it. A notification's
    /// payload is written to outlive the records it names, and this is the case it was written for.
    /// </summary>
    [TestMethod]
    public void FileNameFor_BatchGone_ResolvesTheNameFromNotificationMetadata()
    {
        Dictionary<string, string> recorded = new(StringComparer.OrdinalIgnoreCase) { [GoneBatch] = "conflicting.json" };

        Assert.AreEqual("conflicting.json",
            ImportReview.FileNameFor(NoLiveBatches, recorded, GoneBatch.ToUpperInvariant(), Unresolved),
            "Matched case-insensitively — the action and the alert hold independently-cased copies of one id.");
    }

    /// <summary>
    /// #369: with neither a live batch nor an alert to name it, the row says so — never the batch id.
    /// Replaces #303's <c>FileNameFor_UnknownBatch_FallsBackToTheId</c>, which asserted the id as a
    /// "traceable" fallback: an operator cannot act on a GUID, and a row that cannot be acted on at all
    /// has to say why rather than show something that reads like data.
    /// </summary>
    [TestMethod]
    public void FileNameFor_BatchGoneAndNoNotification_RendersUnresolved()
        => Assert.AreEqual(Unresolved, ImportReview.FileNameFor(NoLiveBatches, NoRecordedNames, GoneBatch, Unresolved),
            "Never the batch id: an operator cannot act on a GUID, and this row cannot be acted on at all.");

    /// <summary>
    /// #369: the lookup is built from every pending-review alert, the dismissed ones included. Pre-#372,
    /// the batches that were orphaned are exactly those whose alert had been marked Obsolete, so a lookup
    /// over active alerts only would miss every name it exists to recover.
    /// </summary>
    [TestMethod]
    public void FileNamesFromNotifications_IncludesDismissedAlerts()
    {
        NotificationEntity active    = ReviewAlert(LiveBatch, "live.json", isDismissed: false);
        NotificationEntity dismissed = ReviewAlert(GoneBatch, "orphaned.json", isDismissed: true);

        IReadOnlyDictionary<string, string> names = ImportReview.FileNamesFromNotifications([active, dismissed]);

        Assert.IsTrue(names.TryGetValue(GoneBatch.ToUpperInvariant(), out string? orphanedName),
            "A dismissed alert carries the only surviving copy of its file's name, and is keyed case-insensitively.");
        Assert.AreEqual("orphaned.json", orphanedName);
        Assert.IsTrue(names.TryGetValue(LiveBatch, out string? liveName));
        Assert.AreEqual("live.json", liveName);
    }

    /// <summary>
    /// #369: the one predicate the page and its tests agree on. A batch is gone when no live row matches
    /// its id — compared case-insensitively, since an action's stored batch id and the batch row's id are
    /// two independently-cased copies of the same value (ADR 012).
    /// </summary>
    [TestMethod]
    public void BatchIsGone_OnlyWhenNoLiveBatchMatches()
    {
        Dictionary<string, string> live = new(StringComparer.OrdinalIgnoreCase) { [LiveBatch] = "live.json" };

        Assert.IsTrue(ImportReview.BatchIsGone(live, GoneBatch), "A batch id with no live row is gone.");
        Assert.IsFalse(ImportReview.BatchIsGone(live, LiveBatch.ToUpperInvariant()),
            "A live batch is not gone, whatever casing the action stored its id in.");
    }

    /// <summary>
    /// #369: Keep existing and Take incoming are impossible once the batch is gone — the decision has
    /// nothing to be applied against — so they are not offered, whatever the row's own conflicts say.
    /// </summary>
    [TestMethod]
    public void CanDecide_BatchGone_IsFalseDespiteAmbiguousFields()
    {
        ImportActionSummaryResponse conflicted = Summary(ImportActionStatus.Pending, GoneBatch, "Quote", "quoteText");

        Assert.IsFalse(ImportReview.CanDecide(conflicted, batchIsGone: true));
        Assert.IsTrue(ImportReview.CanDecide(conflicted, batchIsGone: false),
            "Positive control: the same row with its batch present is decidable. Without it, a CanDecide "
            + "that never offered anything would pass the assertion above.");
    }

    /// <summary>
    /// #369: dismissing is the one thing left to do with a row whose batch is gone, and it discards the
    /// whole batch — every action in it is equally impossible, so they go together.
    /// </summary>
    [TestMethod]
    public async Task DismissBatch_DiscardsTheWholeBatch()
    {
        FakeImportActionService service = new();

        await ImportReview.DismissBatchAsync(service, Summary(ImportActionStatus.Pending, GoneBatch));

        Assert.AreEqual(GoneBatch, service.LastDiscardedBatchId);
        Assert.IsNull(service.LastAppliedBatchId, "Dismissing writes nothing — it is not a decision in disguise.");
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

    /// <summary>
    /// Deciding a row applies its batch. Found in T2 (2026-09-01): the page decided and stopped, so the
    /// action reached <c>Decided</c> and never <c>Applied</c> — the operator's choice never reached the
    /// data, and the alert asking for that choice stayed active because dismissal is wired to apply.
    /// </summary>
    [TestMethod]
    public async Task DecideAndApply_AppliesTheBatchSoTheChoiceReachesTheData()
    {
        string batchId = Guid.NewGuid().ToString("D");
        ImportActionSummaryResponse action = Summary(ImportActionStatus.Pending, batchId, "Quote", "quoteText");
        FakeImportActionService service = new();

        await ImportReview.DecideAndApplyAsync(service, action, FieldResolutionChoice.Replace);

        Assert.AreEqual(batchId, service.LastBulkDecidedBatchId, "The conflicted fields must be decided.");
        Assert.AreEqual(batchId, service.LastAppliedBatchId,
            "Deciding without applying leaves the choice unwritten and the alert active.");
    }

    /// <summary>
    /// An action with nothing in conflict settles nothing, so it must not apply the batch either — a
    /// Blocked action's whole batch is held, and applying would either no-op or write on the strength of
    /// a decision nobody made.
    /// </summary>
    [TestMethod]
    public async Task DecideAndApply_ActionWithNoAmbiguousFields_DoesNothing()
    {
        ImportActionSummaryResponse blocked = Summary(ImportActionStatus.Blocked, Guid.NewGuid().ToString("D"));
        FakeImportActionService service = new();

        await ImportReview.DecideAndApplyAsync(service, blocked, FieldResolutionChoice.Keep);

        Assert.IsNull(service.LastBulkDecidedBatchId);
        Assert.IsNull(service.LastAppliedBatchId);
    }
}
