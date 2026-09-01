using Microsoft.AspNetCore.Components;
using Quotinator.Api.Formatting;
using Quotinator.Api.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Controls;

/// <summary>
/// Shared notification list table (#278) — Created/Type/Message/Expires/Status columns, with optional
/// Action and Dismiss columns. Used by both <see cref="NotificationSummary"/> (the startup-modal
/// summary, both optional columns <see langword="false"/>) and <see cref="Pages.Notifications"/> (the
/// full history page, both <see langword="true"/>) so the two surfaces stay visually consistent. The
/// caller is responsible for deciding what to render when <see cref="Notifications"/> is empty — this
/// component always renders a table, even an empty one. Executing an action always requires an inline
/// confirm/cancel step first — this component never calls <see cref="INotificationActionExecutor"/>
/// itself, only <see cref="ActionExecutor"/>'s read-only <c>CanExecute</c> check; the actual execution
/// is bubbled up via <see cref="OnExecuteAction"/> so the caller controls what happens afterward
/// (reloading the list), matching how <see cref="OnDismiss"/> already works.
/// </summary>
public partial class NotificationTable
{
    #region Public

    /// <summary>The rows to render, in display order.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<NotificationEntity> Notifications { get; set; } = [];

    /// <summary>Whether to render a Dismiss button per undismissed row.</summary>
    [Parameter] public bool ShowDismissAction { get; set; }

    /// <summary>Invoked with a notification's Id when its Dismiss button is clicked. Ignored when <see cref="ShowDismissAction"/> is <see langword="false"/>.</summary>
    [Parameter] public EventCallback<Guid> OnDismiss { get; set; }

    /// <summary>Whether to render an Action column for notifications carrying an executable <c>DismissTriggerKey</c>, per row via <see cref="INotificationActionExecutor.CanExecute"/>.</summary>
    [Parameter] public bool ShowActionColumn { get; set; }

    /// <summary>Invoked with a notification's Id once its action has been confirmed. Ignored when <see cref="ShowActionColumn"/> is <see langword="false"/>.</summary>
    [Parameter] public EventCallback<Guid> OnExecuteAction { get; set; }

    /// <summary>
    /// Raised instead of <see cref="OnExecuteAction"/> for an action with more than one outcome (#303),
    /// carrying which side the operator chose. Separate rather than folded into the callback above so
    /// every existing single-outcome action keeps a signature that cannot express a choice it does not
    /// have.
    /// </summary>
    [Parameter] public EventCallback<(Guid Id, FieldResolutionChoice Choice)> OnExecuteChoiceAction { get; set; }

    /// <summary>
    /// The three mutually-exclusive display states a notification's Status column/filter can show.
    /// Not a persisted column — computed from <see cref="NotificationEntity.IsDismissed"/>/
    /// <see cref="NotificationEntity.ExpiresAt"/> at render time via <see cref="GetDisplayStatus"/>.
    /// </summary>
    /// <summary>Renders a stored UTC timestamp in the host's time zone — see <see cref="LocalTimestamp"/>.</summary>
    /// <param name="utc">The stored UTC value, or <see langword="null"/>.</param>
    internal static string Local(DateTime? utc) => LocalTimestamp.Render(utc);

    internal enum NotificationDisplayStatus { Active, Expired, Dismissed, Resolved, Obsolete, Executing }

    /// <summary>
    /// Classifies a notification's display status: <see cref="NotificationDisplayStatus.Dismissed"/>
    /// takes priority over expiry (an already-dismissed row's expiry no longer matters for display),
    /// then <see cref="NotificationDisplayStatus.Expired"/>, then <see cref="NotificationDisplayStatus.Active"/>.
    /// Mirrors <c>Sql.Notifications.SelectActive</c>'s own active-set definition
    /// (<c>IsDismissed = 0 AND (ExpiresAt IS NULL OR ExpiresAt > @now)</c>) so "Active" here always
    /// means the same thing as the startup modals' own active set.
    /// </summary>
    /// <summary>
    /// Whether the Run control is offered for <paramref name="notification"/>.
    /// </summary>
    /// <remarks>
    /// #367: a running action withdraws the control rather than refusing the click afterwards. A second
    /// session sees the same withdrawal, which is what makes the guard legible instead of silent.
    /// Static and internal so it can be tested without rendering the component — this project has no
    /// bUnit.
    /// </remarks>
    /// <param name="notification">The row being rendered.</param>
    /// <param name="executorCanRun">Whether an executable action is wired up for its trigger.</param>
    /// <param name="isExecuting">Whether this notification's action is running right now.</param>
    internal static bool ShowsRunControl(NotificationEntity notification, bool executorCanRun, bool isExecuting) =>
        !notification.IsDismissed && executorCanRun && !isExecuting;

    internal static NotificationDisplayStatus GetDisplayStatus(NotificationEntity notification, DateTime now, bool isExecuting = false)
    {
        if (notification.IsDismissed)
        {
            // #304: a notification whose action was actually carried out must not read as one the user
            // declined. A row dismissed before the reason column existed has no recorded reason, and
            // keeps the original label rather than being guessed into one bucket or the other.
            // #303: a notification whose subject no longer exists is neither carried out nor declined,
            // and reporting it as either would misstate what happened — the same defect #304's reason
            // column exists to prevent, one case further on.
            return notification.DismissReason.Parsed switch
            {
                NotificationDismissReason.Resolved => NotificationDisplayStatus.Resolved,
                NotificationDismissReason.Obsolete => NotificationDisplayStatus.Obsolete,
                _                                  => NotificationDisplayStatus.Dismissed,
            };
        }
        if (notification.ExpiresAt.Parsed is DateTime expiresAt && expiresAt <= now)
            return NotificationDisplayStatus.Expired;
        // #367: after Dismissed and Expired on purpose. An action dismisses its own notification and
        // only then releases the registry, so a row can be both dismissed and still registered — it
        // must report what happened to it, not what was happening a moment earlier.
        if (isExecuting)
            return NotificationDisplayStatus.Executing;
        return NotificationDisplayStatus.Active;
    }

    /// <summary>Maps a <see cref="NotificationType"/> to its localised display label.</summary>
    internal static string TypeLabel(NotificationType? type, Quotinator.Api.I18nText.UI text) => type switch
    {
        NotificationType.Information    => text.NotificationTypeInformation,
        NotificationType.Warning        => text.NotificationTypeWarning,
        NotificationType.Error          => text.NotificationTypeError,
        NotificationType.Success        => text.NotificationTypeSuccess,
        NotificationType.ActionRequired => text.NotificationTypeActionRequired,
        _                                => "—",
    };

    /// <summary>Maps a <see cref="NotificationType"/> to its Bootstrap badge class.</summary>
    internal static string BadgeClass(NotificationType? type) => type switch
    {
        NotificationType.Information    => "bg-info",
        NotificationType.Warning        => "bg-warning text-dark",
        NotificationType.Error          => "bg-danger",
        NotificationType.Success        => "bg-success",
        NotificationType.ActionRequired => "bg-primary",
        _                                => "bg-secondary",
    };

    #endregion

    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
        Now  = DateTime.UtcNow;
    }

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private INotificationActionExecutor ActionExecutor { get; set; } = default!;

    // #367: read-only here. This component renders the executing state and withdraws the Run control
    // for it; claiming and releasing belong to whichever page actually invokes the executor.
    [Inject] private Quotinator.Api.Startup.NotificationExecutionState Executing { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();
    private DateTime Now;

    /// <summary>The Id of the row currently showing its Confirm/Cancel pair, or <see langword="null"/> if none.</summary>
    private Guid? ConfirmingActionForId;

    private string TypeLabel(NotificationType? type) => TypeLabel(type, Text);

    private bool CanExecuteAction(NotificationEntity notification) =>
        ShowsRunControl(
            notification,
            executorCanRun: notification.DismissTriggerKey.Parsed is NotificationDismissTrigger trigger
                            && ActionExecutor.CanExecute(trigger),
            isExecuting: Executing.IsExecuting(notification.Id));

    private async Task ConfirmActionAsync(Guid id)
    {
        ConfirmingActionForId = null;
        await OnExecuteAction.InvokeAsync(id);
    }

    /// <summary>
    /// #303: a pending-review alert's action has two outcomes rather than one — keep what is stored, or
    /// take what the file brought — so its confirm step offers both instead of a single Confirm.
    /// </summary>
    private static bool OffersResolutionChoice(NotificationEntity notification) =>
        notification.DismissTriggerKey.Parsed == NotificationDismissTrigger.ImportReviewResolved;

    private async Task ConfirmChoiceAsync(Guid id, FieldResolutionChoice choice)
    {
        ConfirmingActionForId = null;
        await OnExecuteChoiceAction.InvokeAsync((id, choice));
    }

    private void CancelAction() => ConfirmingActionForId = null;

    #endregion
}
