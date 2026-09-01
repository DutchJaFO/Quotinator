using Microsoft.AspNetCore.Components;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
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
/// Calls <see cref="IImportActionReader"/> directly — server-side Blazor, same process — matching
/// <see cref="Notifications"/>'s own precedent of sourcing data directly rather than round-tripping
/// through this project's REST endpoints. The decide control arrives with step 9.
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
    public static IEnumerable<ImportActionEntity> AwaitingReview(IEnumerable<ImportActionEntity> actions) =>
        actions.Where(action => action.Status.Parsed is ImportActionStatus.Pending
                                                     or ImportActionStatus.Blocked
                                                     or ImportActionStatus.Stale);

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
    [Inject] private IImportActionReader ActionReader { get; set; } = default!;
    [Inject] private Quotinator.Api.Startup.DatabaseHealthState DatabaseHealth { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();
    private IReadOnlyList<ImportActionEntity> Actions = [];

    private async Task LoadAsync()
    {
        // pageSize 0 is this project's "every matching row as a single page" contract, not an empty
        // page — a review backlog is bounded by what an operator has left undecided, and paging it here
        // would hide rows behind a control this minimal page deliberately does not have.
        PagedItems<ImportActionEntity> page = await ActionReader.GetPagedAsync(
            batchId: null, status: null, entityType: null, page: 1, pageSize: 0);

        Actions = [.. AwaitingReview(page.Items)];
    }

    #endregion
}
