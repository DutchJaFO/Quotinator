using System.Diagnostics;
using Microsoft.AspNetCore.Components;

namespace Quotinator.Api.Components.Pages;

/// <summary>Generic error page. Displays the request ID to aid server-side log correlation.</summary>
public partial class Error
{
    #region Protected

    /// <inheritdoc/>
    protected override void OnInitialized() =>
        RequestId = Activity.Current?.Id ?? HttpContext?.TraceIdentifier;

    #endregion

    #region Private

    [CascadingParameter] private HttpContext? HttpContext { get; set; }

    private string? RequestId { get; set; }
    private bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    #endregion
}
