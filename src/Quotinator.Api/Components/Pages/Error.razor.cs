using System.Diagnostics;
using Microsoft.AspNetCore.Components;

namespace Quotinator.Api.Components.Pages;

public partial class Error
{
    #region Protected

    protected override void OnInitialized() =>
        RequestId = Activity.Current?.Id ?? HttpContext?.TraceIdentifier;

    #endregion

    #region Private

    [CascadingParameter] private HttpContext? HttpContext { get; set; }

    private string? RequestId { get; set; }
    private bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    #endregion
}
