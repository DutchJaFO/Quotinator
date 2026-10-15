using Microsoft.AspNetCore.Components;
using Quotinator.Api.Startup;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Layout;

/// <summary>
/// Top navigation bar. Renders the application menu and the language selector. Health-aware (#263):
/// while the database is unhealthy, Home is disabled and REST API carries a "limited" badge.
/// </summary>
public partial class NavMenu : IDisposable
{
    #region Public

    /// <summary>Stops listening to <see cref="DatabaseHealthState"/>, which outlives this component.</summary>
    public void Dispose() => DatabaseHealth.Changed -= OnHealthChanged;

    #endregion

    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        // #350: a repair runs from a page's own banner, so the badge and the disabled Home link have
        // to follow it. Without this they stay as they were until the next full page load, which is
        // what the repair used to force.
        DatabaseHealth.Changed += OnHealthChanged;
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
    }

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private DatabaseHealthState DatabaseHealth { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    private void OnHealthChanged() => _ = InvokeAsync(StateHasChanged);

    #endregion
}
