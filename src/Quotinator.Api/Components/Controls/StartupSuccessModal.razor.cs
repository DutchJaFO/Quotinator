using Microsoft.AspNetCore.Components;
using Quotinator.Api.Startup;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Controls;

/// <summary>
/// One-time startup-success popup (#263) — database status via <see cref="DatabaseStatsSummary"/>,
/// shown once per process run on <see cref="Pages.Home"/> after a healthy startup. Self-contained: it
/// injects <see cref="StartupUxState"/> directly and dismisses itself, so it needs no
/// <see cref="EventCallback"/> wiring from its parent and no dismiss-on-navigate-away logic — it only
/// ever hides in response to its own Close/Continue button, avoiding the render-mode and
/// prerender-timing bugs the earlier page-replacing interstitial design hit (see #263's plan doc Notes).
/// </summary>
public partial class StartupSuccessModal
{
    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync() =>
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private StartupUxState StartupUx { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    private void Dismiss() => StartupUx.Dismiss();

    #endregion
}
