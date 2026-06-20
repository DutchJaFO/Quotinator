using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Pages;

/// <summary>REST API reference page. Shows endpoints and language support information.</summary>
public partial class RestApi
{
    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
        AdminKeyConfigured = !string.IsNullOrEmpty(Configuration["Quotinator:AdminApiKey"]);
    }

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private IConfiguration Configuration { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();
    private bool AdminKeyConfigured;

    #endregion
}
