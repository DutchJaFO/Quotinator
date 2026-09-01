using Microsoft.AspNetCore.Components;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
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
    [Inject] private Quotinator.Api.Startup.DatabaseHealthState DatabaseHealth { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();
    private IReadOnlyList<ImportActionSummaryResponse> Actions = [];

    // #303, developer feedback from T1: the batch id is correct and meaningless — an operator cannot act
    // on a GUID. Import_Batch.Name is the file name the batch was created from, which is what actually
    // tells them where the conflict came from and which file to go and fix.
    private Dictionary<string, string> BatchFileNames = [];

    private string FileNameFor(string batchId) => FileNameFor(BatchFileNames, batchId);

    /// <summary>
    /// The file a batch was imported from, falling back to the batch id when no batch matches.
    /// </summary>
    /// <remarks>
    /// The fallback is deliberately the id rather than a placeholder: an action whose batch has gone is
    /// an anomaly worth showing something traceable for, and an em dash would hide it. Static and
    /// internal so the mapping can be tested without rendering the component — this project has no
    /// bUnit.
    /// </remarks>
    /// <param name="fileNamesByBatchId">The page's own batch-id to file-name lookup.</param>
    /// <param name="batchId">The action's own batch id.</param>
    internal static string FileNameFor(IReadOnlyDictionary<string, string> fileNamesByBatchId, string batchId) =>
        fileNamesByBatchId.TryGetValue(batchId, out string? name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : batchId;

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

    private async Task DecideAsync(ImportActionSummaryResponse action, FieldResolutionChoice choice)
    {
        await DecideAndApplyAsync(ActionService, action, choice);
        await LoadAsync();
    }

    #endregion
}
