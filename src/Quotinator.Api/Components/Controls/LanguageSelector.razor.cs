using System.Globalization;
using Microsoft.AspNetCore.Components;
using Toolbelt.Blazor.I18nText;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Controls;

public partial class LanguageSelector
{
    #region Protected

    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
    }

    #endregion

    #region Private

    private static readonly (string CultureCode, string Label)[] SupportedLanguages =
    [
        ("en",    "English (en)"),
        ("en-GB", "English UK (en-GB)"),
        ("de",    "Deutsch (de)"),
        ("nl",    "Nederlands (nl)"),
    ];

    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private I18nTextService I18nText { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    private bool IsSelected(string code) =>
        string.Equals(CultureInfo.CurrentUICulture.Name, code, StringComparison.OrdinalIgnoreCase);

    private string ReturnUri =>
        new Uri(Navigation.Uri).GetComponents(UriComponents.PathAndQuery, UriFormat.Unescaped);

    #endregion
}
