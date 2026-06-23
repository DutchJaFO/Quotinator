using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Pages;

/// <summary>The application home page. Shows a random quote.</summary>
public partial class Home
{
    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
        Logger.LogInformation("[UI] home page ready");
    }

    #endregion

    #region Private

    [Inject] private ILogger<Home> Logger { get; set; } = default!;
    [Inject] private I18nTextService I18nText { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    #endregion
}
