using Microsoft.AspNetCore.Components;
using Quotinator.Api.Startup;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Pages;

/// <summary>
/// The application home page. Shows a random quote once the database is healthy; shows the degraded
/// error card instead while it isn't (#263). Startup status (counts, per-file import history) lives
/// permanently on the Statistics page rather than as a one-time interstitial here — see #263's plan
/// doc Notes for why an earlier one-time-summary design was dropped.
/// </summary>
public partial class Home
{
    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync() =>
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private DatabaseHealthState DatabaseHealth { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    // #263: Home is disabled in the nav while unhealthy — QuoteCard fundamentally can't render, so
    // there is no "normal Home" to land in here. Continue instead takes the user to the one page
    // that's actually useful in degraded mode.
    private void ContinueToErrorLayout() => Navigation.NavigateTo("rest-api");

    #endregion
}
