using System.Globalization;
using Microsoft.AspNetCore.Components;
using Quotinator.Api.Components.Controls;
using Quotinator.Api.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Pages;

/// <summary>
/// Permanent notification history page (#278) — every notification (including dismissed/expired),
/// with a Dismiss action per active row and a Status filter (default: active only). Reachable via
/// ordinary navigation, unlike the transient <see cref="Controls.StartupSuccessModal"/>/
/// <see cref="Controls.StartupErrorModal"/> popups, so a user who already closed one of those can
/// still review notification history afterward. Calls <see cref="INotificationReader"/>/
/// <see cref="INotificationWriter"/> directly — server-side Blazor, same process — matching
/// <see cref="Stats"/>'s own precedent of sourcing data directly rather than through this project's
/// own REST endpoints. Row rendering itself is shared with <see cref="Controls.NotificationSummary"/>
/// via <see cref="Controls.NotificationTable"/>.
/// </summary>
public partial class Notifications
{
    #region Public

    /// <summary>The three ways this page can restrict which notifications are shown.</summary>
    internal enum NotificationFilterMode { Active, All, ExpiredOnly }

    #endregion

    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);

        // #326: this route is exempt from DatabaseHealthGateMiddleware, so it is reachable precisely
        // when the database is not — and LoadAsync is a live query that throws SQLITE_CANTOPEN past
        // NotificationReader's missing-table catch when the data directory cannot be written. Rendering
        // an empty list is the degraded answer; crashing the page is not one. Same gate as
        // DatabaseStatsSummary (#293) and NotificationSummary.
        if (!DatabaseHealth.IsHealthy)
        {
            AllNotifications = [];
            Now = DateTime.UtcNow;
            return;
        }

        await LoadAsync();
    }

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private INotificationReader NotificationReader { get; set; } = default!;
    [Inject] private Quotinator.Api.Startup.DatabaseHealthState DatabaseHealth { get; set; } = default!;
    [Inject] private INotificationWriter NotificationWriter { get; set; } = default!;
    [Inject] private INotificationActionExecutor ActionExecutor { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();
    private IReadOnlyList<NotificationEntity> AllNotifications = [];
    private DateTime Now;
    private NotificationFilterMode Filter = NotificationFilterMode.Active;

    private IReadOnlyList<NotificationEntity> FilteredNotifications =>
        [.. AllNotifications.Where(MatchesFilter)];

    private bool MatchesFilter(NotificationEntity notification)
    {
        NotificationTable.NotificationDisplayStatus status = NotificationTable.GetDisplayStatus(notification, Now);
        return Filter switch
        {
            NotificationFilterMode.Active      => status == NotificationTable.NotificationDisplayStatus.Active,
            NotificationFilterMode.ExpiredOnly => status == NotificationTable.NotificationDisplayStatus.Expired,
            _                                   => true,
        };
    }

    private async Task LoadAsync()
    {
        // #319: same rule as the startup popup — the page renders in the UI language, so its
        // notifications resolve to that language too, falling back to each one's original.
        PagedItems<NotificationEntity> page = await NotificationReader.GetPagedAsync(
            1, 0, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        AllNotifications = page.Items;
        Now = DateTime.UtcNow;
    }

    private async Task DismissAsync(Guid id)
    {
        await NotificationWriter.DismissAsync(id);
        await LoadAsync();
    }

    private async Task ExecuteActionAsync(Guid id)
    {
        NotificationEntity? notification = AllNotifications.FirstOrDefault(n => n.Id == id);
        if (notification?.DismissTriggerKey.Parsed is NotificationDismissTrigger trigger)
        {
            // The row's own MetadataKind selects the type its payload reads back as, so the action
            // receives the producer's own shape without this page knowing which producer wrote it.
            NotificationMetadataDto? metadata =
                NotificationMetadataKinds.TryDeserialize(notification.MetadataKind.Parsed, notification.Metadata);

            await ActionExecutor.ExecuteAsync(trigger, metadata);
        }

        await LoadAsync();
    }

    private void SetFilter(NotificationFilterMode mode) => Filter = mode;

    private string FilterButtonClass(NotificationFilterMode mode) =>
        Filter == mode ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-primary";

    #endregion
}
