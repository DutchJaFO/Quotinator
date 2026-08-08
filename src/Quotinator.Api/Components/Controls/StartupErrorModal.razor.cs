using Microsoft.AspNetCore.Components;
using Quotinator.Api.Startup;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Controls;

/// <summary>
/// Degraded startup error popup (#263) — the failure reason, the last known-good database status
/// (the state #254's backup/restore safety net left the database at), and a button through to the
/// degraded error layout. Shown by <see cref="Pages.Home"/> while <see cref="DatabaseHealthState.IsHealthy"/>
/// is false. Rendered as a modal overlay, matching <see cref="StartupSuccessModal"/>'s presentation —
/// unlike that one, it has no dismiss state of its own; it is shown for as long as Home renders it.
/// </summary>
public partial class StartupErrorModal
{
    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync() =>
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);

    #endregion

    #region Public

    /// <summary>Invoked when the user clicks through to the degraded error layout.</summary>
    [Parameter] public EventCallback OnContinue { get; set; }

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private DatabaseHealthState DatabaseHealth { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    private string? FailureReason => DatabaseHealth.FailureReason;

    #endregion
}
