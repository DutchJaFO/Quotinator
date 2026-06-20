using Microsoft.AspNetCore.Components;
using Quotinator.Changelog.Services;
using Quotinator.Core.Services;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Pages;

/// <summary>About page. Shows project information, roadmap, and changelog.</summary>
public partial class About
{
    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
    }

    #endregion

    #region Private

    [Inject] private IVersionService VersionService { get; set; } = default!;
    [Inject] private IChangelogService ChangelogService { get; set; } = default!;
    [Inject] private I18nTextService I18nText { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    #endregion
}
