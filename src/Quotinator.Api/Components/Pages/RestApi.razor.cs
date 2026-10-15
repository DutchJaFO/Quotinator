using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Quotinator.Api.Startup;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Pages;

/// <summary>
/// REST API reference page. Shows endpoints and language support information. Shows a degraded-state
/// banner (#263) while the database is unhealthy.
/// </summary>
public partial class RestApi : IDisposable
{
    #region Public

    /// <summary>Stops listening to <see cref="DatabaseHealthState"/>, which outlives this component.</summary>
    public void Dispose() => DatabaseHealth.Changed -= OnHealthChanged;

    #endregion

    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        // #350: this page is where the modal's Continue lands and where a repair is most likely to be
        // run, so its own limited-functionality banner has to clear when the repair succeeds.
        DatabaseHealth.Changed += OnHealthChanged;
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
        AdminKeyConfigured = !string.IsNullOrEmpty(Configuration["Quotinator:AdminApiKey"]);
    }

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private IConfiguration Configuration { get; set; } = default!;
    [Inject] private DatabaseHealthState DatabaseHealth { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();
    private bool AdminKeyConfigured;

    private void OnHealthChanged() => _ = InvokeAsync(StateHasChanged);

    #endregion
}
