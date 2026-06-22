using Microsoft.AspNetCore.Components;
using Quotinator.Changelog.Models;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Controls;

/// <summary>Renders the unreleased changelog block as a collapsible details block, always expanded.</summary>
public partial class ChangelogUnreleasedEntry
{
    #region Public

    /// <summary>The unreleased block to render.</summary>
    [Parameter, EditorRequired] public ChangelogUnreleased Unreleased { get; set; } = default!;

    #endregion

    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
    }

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    private bool HasReferences => Unreleased.Issues.Count > 0 || Unreleased.Cves.Count > 0;

    #endregion
}
