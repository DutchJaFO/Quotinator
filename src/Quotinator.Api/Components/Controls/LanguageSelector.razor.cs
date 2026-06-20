using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Quotinator.Constants.Routes;
using Toolbelt.Blazor.I18nText;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Controls;

/// <summary>Navbar language selector. Submits a form to set the culture cookie and redirect back to the current page.</summary>
public partial class LanguageSelector
{
    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
    }

    #endregion

    #region Private

    // Property (not field) because it references Text, which is set in OnInitializedAsync.
    private (string CultureCode, string Label)[] SupportedLanguages =>
    [
        ("",      Text.LanguageSelectorAutoDetect),
        ("en-GB", "English (en-GB)"),
        ("de",    "Deutsch (de)"),
        ("nl",    "Nederlands (nl)"),
    ];

    [CascadingParameter] private HttpContext? HttpContext { get; set; }
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private I18nTextService I18nText { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    // Auto-detect mode: no culture cookie is set, so the browser's Accept-Language drives the language.
    private bool IsAutoMode =>
        string.IsNullOrEmpty(HttpContext?.Request.Cookies[CookieRequestCultureProvider.DefaultCookieName]);

    private bool IsSelected(string cultureCode) =>
        cultureCode.Length == 0
            ? IsAutoMode
            : !IsAutoMode && string.Equals(CultureInfo.CurrentUICulture.Name, cultureCode, StringComparison.OrdinalIgnoreCase);

    // Includes PathBase so the form action works through HA ingress (e.g. /api/hassio_ingress/TOKEN/Culture/Set).
    // Without PathBase the browser sends to /Culture/Set, which bypasses the ingress proxy entirely.
    private string CultureSetAction =>
        (HttpContext?.Request.PathBase.Value ?? string.Empty).TrimEnd('/') + ApiRoutes.CultureSet;

    private string ReturnUri =>
        new Uri(Navigation.Uri).GetComponents(UriComponents.PathAndQuery, UriFormat.Unescaped);

    #endregion
}
