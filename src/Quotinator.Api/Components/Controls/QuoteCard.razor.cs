using Microsoft.AspNetCore.Components;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Toolbelt.Blazor.I18nText;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Controls;

/// <summary>Displays a single random quote with attribution and a button to load the next one.</summary>
public partial class QuoteCard
{
    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
        LoadQuote();
    }

    #endregion

    #region Private

    [Inject] private IQuoteService QuoteService { get; set; } = default!;
    [Inject] private I18nTextService I18nText { get; set; } = default!;

    private QuoteResponse? _quote;
    private Quotinator.Api.I18nText.UI Text = new();

    private void LoadQuote() =>
        _quote = QuoteService.GetRandom(1).Items.FirstOrDefault();

    private string Attribution =>
        _quote?.Character ?? _quote?.Author ?? string.Empty;

    private string DateSuffix =>
        _quote?.Date is { } d ? $" ({d[..4]})" : string.Empty;

    #endregion
}
