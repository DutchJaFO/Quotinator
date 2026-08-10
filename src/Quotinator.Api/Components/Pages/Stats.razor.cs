using Microsoft.AspNetCore.Components;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Pages;

/// <summary>
/// Permanent database statistics page (#263) — the same counts and per-file seed report shown
/// once at startup, kept reachable via nav afterward. Renders unconditionally, since
/// <see cref="Quotinator.Data.Database.IDatabaseInitializer"/>'s data stays meaningful (the last
/// known-good snapshot) even while the database is degraded.
/// </summary>
public partial class Stats
{
    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync() =>
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();

    #endregion
}
