using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Quotinator.Changelog.Models;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Controls;

/// <summary>Renders a single changelog release entry as a collapsible details block.</summary>
public partial class ChangelogEntry
{
    #region Public

    /// <summary>The release to render.</summary>
    [Parameter, EditorRequired] public ChangelogRelease Release { get; set; } = default!;

    /// <summary>Zero-based position in the release list; entry 0 is expanded by default.</summary>
    [Parameter] public int Index { get; set; }

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

    private bool IsOpen => Index == 0;

    internal static MarkupString FormatInline(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        var result  = Regex.Replace(encoded, @"\[([^\]]+)\]\(([^)]+)\)", "<a href=\"$2\">$1</a>");
        result      = Regex.Replace(result,  @"`([^`]+)`",               "<code>$1</code>");
        return new MarkupString(result);
    }

    internal static string CategoryBadgeClass(string category) => category switch
    {
        "Added"   => "bg-success",
        "Fixed"   => "bg-danger",
        "Changed" => "bg-primary",
        "Removed" => "bg-warning text-dark",
        _         => "bg-secondary"
    };

    #endregion
}
