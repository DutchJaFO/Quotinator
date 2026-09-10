using Microsoft.AspNetCore.Components;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Pages;

/// <summary>
/// Minimal import-review page (#303) — every staged action still awaiting a human decision, across all
/// batches, with a basic decide control per row.
/// <para>
/// Deliberately not the side-by-side diff/merge editor #66 envisions: no field-level comparison view,
/// no bulk actions, no inline merge. This exists so the alert #303 raises has somewhere to point, and
/// so an operator can clear a backlog without reaching for curl.
/// </para>
/// <para>
/// Calls <see cref="IImportActionService"/> directly — server-side Blazor, same process — matching
/// <see cref="Notifications"/>'s own precedent of sourcing data directly rather than round-tripping
/// through this project's REST endpoints.
/// </para>
/// </summary>
public partial class ImportReview
{
    #region Public

    /// <summary>
    /// The statuses that represent work still waiting on a person. Public and static so it can be
    /// unit-tested without rendering the component — this project has no bUnit.
    /// </summary>
    /// <remarks>
    /// <c>Decided</c>, <c>Applied</c> and <c>Discarded</c> are all finished states: something has
    /// already happened to them, and listing them would present settled work as outstanding. A row
    /// whose stored status cannot be parsed is excluded rather than assumed reviewable — it is data
    /// this application did not write, and guessing would put phantom work on the page.
    /// </remarks>
    /// <param name="actions">Every action to consider.</param>
    public static IEnumerable<ImportActionSummaryResponse> AwaitingReview(IEnumerable<ImportActionSummaryResponse> actions) =>
        actions.Where(action =>
            Enum.TryParse(action.Status, out ImportActionStatus status)
            && status is ImportActionStatus.Pending or ImportActionStatus.Blocked or ImportActionStatus.Stale);

    /// <summary>
    /// The field-level rows a whole-action decision resolves — one per field actually in conflict, all
    /// carrying <paramref name="choice"/>.
    /// </summary>
    /// <remarks>
    /// Only <see cref="ImportActionSummaryResponse.AmbiguousFields"/>, never every decidable field. This
    /// is the degenerate case of the git model this page is eventually meant to become: <c>--ours</c>
    /// and <c>--theirs</c> resolve the conflicted hunks and leave the rest of the merge alone. Deciding
    /// every field would overwrite ones nobody was asked about, including nulling a field the incoming
    /// file simply does not carry.
    /// <para>
    /// Returns nothing for an action with no ambiguous fields — a <c>Blocked</c> action is held because
    /// it would touch a protected field, not because two values disagree, so a whole-action decision has
    /// nothing to resolve for it.
    /// </para>
    /// </remarks>
    /// <param name="action">The action being decided.</param>
    /// <param name="choice">Which side wins for every conflicted field.</param>
    public static IEnumerable<ImportActionFieldRowDto> DecisionRows(ImportActionSummaryResponse action, FieldResolutionChoice choice) =>
        action.AmbiguousFields.Select(field => new ImportActionFieldRowDto
        {
            ActionId   = action.Id,
            EntityId   = action.EntityId,
            EntityType = action.EntityType,
            Field      = field,
            Decision   = choice,
        });

    #endregion

    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);

        // Same gate as Notifications (#326): this route is exempt from DatabaseHealthGateMiddleware, so
        // it is reachable precisely when the database is not. Rendering an empty list is the degraded
        // answer; letting a live query throw past the page is not one.
        if (!DatabaseHealth.IsHealthy)
        {
            Actions = [];
            return;
        }

        await LoadAsync();
    }

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private IImportActionService ActionService { get; set; } = default!;
    [Inject] private IImportBatchRepository ImportBatches { get; set; } = default!;
    [Inject] private INotificationReader NotificationReader { get; set; } = default!;
    [Inject] private Quotinator.Api.Startup.DatabaseHealthState DatabaseHealth { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();
    private IReadOnlyList<ImportActionSummaryResponse> Actions = [];

    // #303, developer feedback from T1: the batch id is correct and meaningless — an operator cannot act
    // on a GUID. Import_Batch.Name is the file name the batch was created from, which is what actually
    // tells them where the conflict came from and which file to go and fix.
    private Dictionary<string, string> BatchFileNames = [];

    // #369: the file name each pending-review alert recorded, by batch id. An alert's payload is written
    // to outlive the batch it names, so this is where the name of a batch that is gone survives.
    private IReadOnlyDictionary<string, string> RecordedFileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private string FileNameFor(string batchId) =>
        FileNameFor(BatchFileNames, RecordedFileNames, batchId, Text.ImportReviewBatchGone);

    private bool BatchIsGone(string batchId) => BatchIsGone(BatchFileNames, batchId);

    /// <summary>
    /// The file a row's batch was imported from: the live batch's own name, else the name its
    /// pending-review alert recorded, else <paramref name="unresolved"/> (#369).
    /// </summary>
    /// <remarks>
    /// The live batch wins where both exist — it is the record itself, and the alert only a copy taken
    /// when it was raised. Never the batch id: an operator cannot act on a GUID, and a row whose batch is
    /// gone with no alert to name it — one staged before #303 shipped — has nothing truthful to show but
    /// that fact. Static and internal so the mapping can be tested without rendering the component —
    /// this project has no bUnit.
    /// </remarks>
    /// <param name="liveBatchFileNames">Every batch that still exists, by id.</param>
    /// <param name="recordedFileNames">The file name each pending-review alert recorded, by batch id.</param>
    /// <param name="batchId">The action's own batch id.</param>
    /// <param name="unresolved">What to show when neither source names the file.</param>
    internal static string FileNameFor(
        IReadOnlyDictionary<string, string> liveBatchFileNames,
        IReadOnlyDictionary<string, string> recordedFileNames,
        string batchId,
        string unresolved)
    {
        if (liveBatchFileNames.TryGetValue(batchId, out string? live) && !string.IsNullOrWhiteSpace(live))
            return live;
        if (recordedFileNames.TryGetValue(batchId, out string? recorded) && !string.IsNullOrWhiteSpace(recorded))
            return recorded;
        return unresolved;
    }

    /// <summary>
    /// Every batch id a pending-review alert has named, mapped to the file name that alert recorded (#369).
    /// </summary>
    /// <remarks>
    /// Keyed case-insensitively, since an action's stored batch id and the alert's copy of it are two
    /// independently-cased values (ADR 012). A payload that cannot be read contributes nothing rather than
    /// throwing — a row written by an older build must not take the page down.
    /// </remarks>
    /// <param name="notifications">Pending-review alerts, dismissed ones included.</param>
    internal static IReadOnlyDictionary<string, string> FileNamesFromNotifications(IEnumerable<NotificationEntity> notifications)
    {
        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (NotificationEntity notification in notifications)
        {
            if (NotificationMetadataKinds.TryDeserialize(notification.MetadataKind.Parsed, notification.Metadata)
                    is ImportReviewPendingMetadataDto review
                && !string.IsNullOrWhiteSpace(review.FileName))
            {
                names.TryAdd(review.BatchId, review.FileName);
            }
        }
        return names;
    }

    /// <summary>Whether <paramref name="batchId"/> matches no batch that still exists (#369).</summary>
    /// <remarks>
    /// The one predicate the page and its tests agree on. Derived at render time from the page's own
    /// batch lookup rather than stored: the absence of the row is already the fact, which is why no status
    /// or schema change was needed — and ADR 014 rules out a stored flag for a dangling reference. As
    /// case-insensitive as <paramref name="liveBatchFileNames"/>' own comparer, which the page builds that
    /// way.
    /// </remarks>
    /// <param name="liveBatchFileNames">Every batch that still exists, by id.</param>
    /// <param name="batchId">The action's own batch id.</param>
    internal static bool BatchIsGone(IReadOnlyDictionary<string, string> liveBatchFileNames, string batchId) =>
        !liveBatchFileNames.ContainsKey(batchId);

    /// <summary>Whether a whole-action Keep/Take is offered for <paramref name="action"/> (#369).</summary>
    /// <remarks>
    /// Never once the batch is gone, whatever the row's own conflicts say: the decision would have
    /// nothing to be applied against. Removed rather than disabled — impossible, not unavailable.
    /// </remarks>
    /// <param name="action">The row being rendered.</param>
    /// <param name="batchIsGone">Whether the row's batch no longer exists.</param>
    internal static bool CanDecide(ImportActionSummaryResponse action, bool batchIsGone) =>
        !batchIsGone && action.AmbiguousFields.Count > 0;

    private bool CanDecide(ImportActionSummaryResponse action) => CanDecide(action, BatchIsGone(action.BatchId));

    private async Task LoadAsync()
    {
        // The service, not IImportActionReader: the summary it returns carries AmbiguousFields, which is
        // what a whole-action decision resolves. The reader returns raw entities, which do not.
        //
        // pageSize 0 is this project's "every matching row as a single page" contract, not an empty
        // page — a review backlog is bounded by what an operator has left undecided, and paging it here
        // would hide rows behind a control this minimal page deliberately does not have.
        PagedItems<ImportActionSummaryResponse> page = await ActionService.GetPagedAsync(
            batchId: null, status: null, entityType: null, page: 1, pageSize: 0);

        Actions = [.. AwaitingReview(page.Items)];

        // One read for the whole page rather than one per row — the batch count is small, and a lookup
        // per action would be an N+1 against a table this page already knows it needs in full.
        IReadOnlyList<ImportBatchEntity> batches = await ImportBatches.GetAllAsync();
        BatchFileNames = batches
            .GroupBy(batch => batch.Id.ToCanonicalId(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);

        // #369: one read for the page as well, and over the alert history rather than the active set —
        // a batch that is gone is exactly one whose alert may already have been dismissed.
        IReadOnlyList<NotificationEntity> alerts =
            await NotificationReader.GetByMetadataKindAsync(NotificationMetadataKind.ImportReviewPending);
        RecordedFileNames = FileNamesFromNotifications(alerts);
    }

    /// <summary>
    /// Decides <paramref name="action"/>'s conflicted fields and applies its batch.
    /// </summary>
    /// <remarks>
    /// Internal and static so the sequence can be tested without rendering the component — this project
    /// has no bUnit, the same reason <see cref="AwaitingReview"/> and <see cref="DecisionRows"/> are
    /// shaped this way.
    /// </remarks>
    /// <param name="service">The service both steps go through.</param>
    /// <param name="action">The action being decided.</param>
    /// <param name="choice">Which side wins for every conflicted field.</param>
    internal static async Task DecideAndApplyAsync(
        IImportActionService service,
        ImportActionSummaryResponse action,
        FieldResolutionChoice choice)
    {
        List<ImportActionFieldRowDto> rows = [.. DecisionRows(action, choice)];

        // Nothing in conflict means nothing this control can settle — a Blocked action needs its
        // completeness hold lifted, which is #66's per-item UX, not a whole-action keep/take.
        if (rows.Count == 0) return;

        await service.BulkDecideAsync(action.BatchId, rows);

        // Deciding stages the choice; it does not write it. Applying is the completion of the decision
        // the operator just made, not a second one taken on their behalf — and it is what dismisses the
        // alert, which is wired to ApplyBatchAsync rather than to deciding. TryApplyBatchAsync writes
        // nothing while any action in the batch is still Pending/Blocked/Stale, so calling it after each
        // row is a no-op until the last one is settled and atomic when it is.
        await service.ApplyBatchAsync(action.BatchId);
    }

    /// <summary>
    /// Discards <paramref name="action"/>'s whole batch — the one thing left to do with a row whose batch
    /// is gone (#369).
    /// </summary>
    /// <remarks>
    /// <see cref="IImportActionService.DiscardBatchAsync"/> reads and writes <c>Import_Action</c> only and
    /// never the batch row, so it is already correct against a missing parent; it also retires the
    /// batch's alert as resolved, since the operator dealt with it by keeping none of it. Internal and
    /// static for the same reason as <see cref="DecideAndApplyAsync"/>.
    /// </remarks>
    /// <param name="service">The service the discard goes through.</param>
    /// <param name="action">The row being dismissed.</param>
    internal static Task DismissBatchAsync(IImportActionService service, ImportActionSummaryResponse action) =>
        service.DiscardBatchAsync(action.BatchId);

    private async Task DismissAsync(ImportActionSummaryResponse action)
    {
        await DismissBatchAsync(ActionService, action);
        await LoadAsync();
    }

    private async Task DecideAsync(ImportActionSummaryResponse action, FieldResolutionChoice choice)
    {
        await DecideAndApplyAsync(ActionService, action, choice);
        await LoadAsync();
    }

    #endregion
}
