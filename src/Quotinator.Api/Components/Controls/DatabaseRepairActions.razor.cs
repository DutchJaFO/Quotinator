using Microsoft.AspNetCore.Components;
using Quotinator.Api.Services;
using Quotinator.Api.Startup;
using Quotinator.Data.Enums;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Controls;

/// <summary>
/// The repair a degraded application offers its operator (#350): a database Reset, armed by a confirm
/// step, run in-process. Rendered by <see cref="StartupErrorModal"/> and by the degraded pages an
/// operator reaches after dismissing it, so the remedy the failure reason names is reachable from
/// wherever they are rather than only from the startup modal.
/// <para>
/// A restore from a backup is the second remedy the reason names and is not offered here: no restore
/// endpoint exists yet, and an operator performs one by hand. It joins this control when it is built.
/// </para>
/// <para>
/// This control never reads the database. While degraded its content is untrusted, which is why the
/// notification carrying this same action on the Notifications page is not rendered and may never have
/// been written; everything here is in-memory state or the executor.
/// </para>
/// </summary>
public partial class DatabaseRepairActions
{
    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync() =>
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);

    #endregion

    #region Public

    /// <summary>
    /// What the operator is told after the repair ran, or <see langword="null"/> when it ran and there
    /// is nothing left to say, because the application is healthy and the page reloads into it.
    /// </summary>
    /// <param name="result">The outcome the action returned.</param>
    internal static string? RepairOutcomeText(NotificationActionResult result) =>
        result switch
        {
            { Succeeded: true }                       => null,
            { BackupObstacle: BackupOutcome obstacle } =>
                $"The database was not reset: no backup could be taken first ({obstacle}). "
                + BackupObstacleGuidance.Cause(obstacle)
                + " Nothing was changed, so the database is exactly as it was.",
            _                                         =>
                "The database was not reset, and no reason was reported. Resolve it from the admin "
                + "endpoints, or restore a backup taken before the version moved ahead.",
        };

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private INotificationActionExecutor ActionExecutor { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    private bool ConfirmingReset;
    private bool Repairing;
    private string? RepairOutcome;

    /// <summary>
    /// Runs the Reset through the same executor the Notifications page uses, so the sequence (back up,
    /// rebuild, restore health, re-record the version, re-run the condition checks) stays in one place
    /// rather than being written a second time here.
    /// </summary>
    private async Task ConfirmResetAsync()
    {
        ConfirmingReset = false;
        Repairing       = true;
        RepairOutcome   = null;

        try
        {
            RepairOutcome = RepairOutcomeText(
                await ActionExecutor.ExecuteAsync(NotificationDismissTrigger.DatabaseReset));
        }
        finally
        {
            Repairing = false;
        }

        // A reset that ran leaves the application healthy, and every surface rendering this control
        // exists only while it is not: reload so the normal UI replaces it rather than leaving a dead
        // degraded page on screen.
        if (RepairOutcome is null) Navigation.NavigateTo(Navigation.Uri, forceLoad: true);
    }

    #endregion
}
