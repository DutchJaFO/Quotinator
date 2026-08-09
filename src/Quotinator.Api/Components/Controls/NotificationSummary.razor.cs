using Microsoft.AspNetCore.Components;
using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Controls;

/// <summary>
/// Active-notification summary (#278) — every undismissed, unexpired notification regardless of
/// type, embedded in both <see cref="StartupSuccessModal"/> and <see cref="StartupErrorModal"/>
/// alongside <see cref="DatabaseStatsSummary"/>. Which modal renders is already determined by
/// <c>DatabaseHealthState</c>, not by notification type — a Warning/Error notification unrelated to
/// database health (e.g. "backup skipped due to low disk space") must still be visible during a
/// perfectly healthy startup, so both modals show the same unfiltered active set rather than each
/// showing only a subset of types. Display-only: dismissing a notification is done from
/// <see cref="Pages.Notifications"/>, not here.
/// </summary>
public partial class NotificationSummary
{
    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
        Notifications = await NotificationReader.GetActiveNotificationsAsync();
    }

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private INotificationReader NotificationReader { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();
    private IReadOnlyList<NotificationEntity> Notifications = [];

    #endregion
}
