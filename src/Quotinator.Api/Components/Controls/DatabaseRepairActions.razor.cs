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
/// endpoint exists yet, and an operator performs one by hand. It joins this control when #444 builds
/// it.
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
    /// <remarks>
    /// The sentences around the obstacle come from the translation files. The obstacle's own cause and
    /// remedies come from <see cref="ResetObstacleGuidance"/> and <see cref="BackupObstacleGuidance"/>,
    /// which the API's problem responses share and which are English for now (#333).
    /// </remarks>
    /// <param name="result">The outcome the action returned.</param>
    /// <param name="text">The translated strings for the current culture.</param>
    internal static string? RepairOutcomeText(NotificationActionResult result, Quotinator.Api.I18nText.UI text) =>
        result switch
        {
            { Succeeded: true }                       => null,
            { ResetObstacle: ResetObstacle obstacle } =>
                WithObstacle(text.DatabaseRepairRefusedForReset, obstacle.ToString()) + " "
                + ResetObstacleGuidance.Cause(obstacle) + " "
                + string.Join(" ", ResetObstacleGuidance.Remedies(obstacle)),
            { BackupObstacle: BackupOutcome obstacle } =>
                WithObstacle(text.DatabaseRepairRefusedForBackup, obstacle.ToString()) + " "
                + BackupObstacleGuidance.Cause(obstacle) + " "
                + text.DatabaseRepairNothingChanged,
            { FailureReason: string reason }           =>
                text.DatabaseRepairResetNotIntact + " " + reason,
            _                                         => text.DatabaseRepairNoReasonReported,
        };

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private INotificationActionExecutor ActionExecutor { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    // A plain Replace rather than string.Format: the template comes from a translation file, and a
    // placeholder typo there must render as text rather than throw (CLAUDE.md's IApiLocalizer.Format rule).
    private static string WithObstacle(string template, string obstacle) =>
        template.Replace("{0}", obstacle, StringComparison.Ordinal);

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
                await ActionExecutor.ExecuteAsync(NotificationDismissTrigger.DatabaseReset), Text);
        }
        finally
        {
            Repairing = false;
        }

        // A reset that ran leaves the application healthy, and every surface rendering this control
        // exists only while it is not. They learn that from DatabaseHealthState.Changed, which
        // MarkHealthy raises, and re-render themselves.
        //
        // This used to be Navigation.NavigateTo(Uri, forceLoad: true). A full reload of an interactive
        // page tears the Blazor circuit down without a close handshake: measured 2026-10-14, four
        // WebSocketExceptions at Error per reload, so a repair that worked wrote four error lines into
        // the log an operator reads to find out what is wrong.
    }

    #endregion
}
