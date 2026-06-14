using Microsoft.AspNetCore.Components;
using Toolbelt.Blazor.I18nText;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Layout;

public partial class NavMenu
{
    #region Protected

    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
    }

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    #endregion
}
