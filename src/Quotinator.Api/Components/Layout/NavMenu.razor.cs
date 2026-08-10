using Microsoft.AspNetCore.Components;
using Quotinator.Api.Startup;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Layout;

/// <summary>
/// Top navigation bar. Renders the application menu and the language selector. Health-aware (#263):
/// while the database is unhealthy, Home is disabled and REST API carries a "limited" badge.
/// </summary>
public partial class NavMenu
{
    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
    }

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private DatabaseHealthState DatabaseHealth { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    #endregion
}
