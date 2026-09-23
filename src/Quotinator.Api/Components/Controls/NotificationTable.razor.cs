using System.Globalization;
using Microsoft.AspNetCore.Components;
using Quotinator.Api.Enums;
using Quotinator.Api.Formatting;
using Quotinator.Api.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;
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

    /// <summary>
    /// #308: whether payload detail opens in a dialog over this surface rather than expanding in place.
    /// </summary>
    /// <remarks>
    /// <see langword="true"/> for the startup modal only (developer decision, 2026-09-02). Expanding a
    /// row in place inside a size-constrained popup pushes the rest of the list out of view; the
    /// `/notifications` page has the room and uses a disclosure element instead.
    /// </remarks>
    [Parameter] public bool DetailAsDialog { get; set; }

    /// <summary>Invoked with a notification's Id when its Dismiss button is clicked. Ignored when <see cref="ShowDismissAction"/> is <see langword="false"/>.</summary>
    [Parameter] public EventCallback<Guid> OnDismiss { get; set; }

    /// <summary>Whether to render an Action column for notifications carrying an executable <c>DismissTriggerKey</c>, per row via <see cref="INotificationActionExecutor.CanExecute(NotificationDismissTrigger, NotificationMetadataDto, NotificationActionAvailability)"/>.</summary>
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
    /// #369: the volatile state this render's actions depend on, read once by the caller and handed to
    /// every row — so no row runs a query of its own.
    /// </summary>
    [Parameter, EditorRequired] public NotificationActionAvailability Availability { get; set; } = default!;

    /// <summary>Renders a stored UTC timestamp in the host's time zone — see <see cref="LocalTimestamp"/>.</summary>
    /// <param name="utc">The stored UTC value, or <see langword="null"/>.</param>
    internal static string Local(DateTime? utc) => LocalTimestamp.Render(utc);

    /// <summary>
    /// Whether <paramref name="notification"/>'s action can still be run, asked of
    /// <paramref name="executor"/> with the row's own payload and this render's
    /// <paramref name="availability"/> (#369).
    /// </summary>
    /// <remarks>
    /// The trigger alone cannot answer this — it says an action is wired up, not that the thing it acts on
    /// still exists. Static and internal so it can be tested without rendering the component — this
    /// project has no bUnit.
    /// </remarks>
    /// <param name="executor">The executor whose capability check decides.</param>
    /// <param name="notification">The row being rendered.</param>
    /// <param name="availability">The volatile state read once for this render.</param>
    internal static bool ExecutorCanRun(INotificationActionExecutor executor, NotificationEntity notification, NotificationActionAvailability availability) =>
        notification.DismissTriggerKey.Parsed is NotificationDismissTrigger trigger
        && executor.CanExecute(
            trigger,
            NotificationMetadataKinds.TryDeserialize(notification.MetadataKind.Parsed, notification.Metadata),
            availability);

    /// <summary>
    /// Whether <paramref name="notification"/> carries an action that exists but can no longer run —
    /// the fact <see cref="NotificationDisplayStatus.ActionUnavailable"/> reports (#369).
    /// </summary>
    /// <remarks>
    /// A row with no action at all is not "unavailable": it never had one to lose. Static and internal so
    /// it can be tested without rendering the component — this project has no bUnit.
    /// </remarks>
    /// <param name="executor">The executor whose capability checks decide.</param>
    /// <param name="notification">The row being rendered.</param>
    /// <param name="availability">The volatile state read once for this render.</param>
    internal static bool ActionIsUnavailable(INotificationActionExecutor executor, NotificationEntity notification, NotificationActionAvailability availability) =>
        notification.DismissTriggerKey.Parsed is NotificationDismissTrigger trigger
        && executor.CanExecute(trigger)
        && !ExecutorCanRun(executor, notification, availability);

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

    /// <summary>
    /// Whether the Dismiss control is offered for <paramref name="notification"/>.
    /// </summary>
    /// <remarks>
    /// #367, found in T1: withdrawn while the action runs. There is nothing to dismiss — the operator
    /// already chose to act — and the click does not merely do nothing. Blazor serialises circuit
    /// events, so it queues behind the running handler and is applied <em>after</em> the action has
    /// recorded <c>Resolved</c>, overwriting it with <c>Dismissed</c>: a carried-out action then reads
    /// as one the user declined, which is the defect #304 exists to prevent.
    /// </remarks>
    /// <param name="notification">The row being rendered.</param>
    /// <param name="isExecuting">Whether this notification's action is running right now.</param>
    internal static bool ShowsDismissControl(NotificationEntity notification, bool isExecuting) =>
        !notification.IsDismissed && !isExecuting;

    /// <summary>A notification's payload detail as a table. #308.</summary>
    /// <param name="Headers">Column headings, already localised.</param>
    /// <param name="Rows">One row per payload entry, cells in the same order as <paramref name="Headers"/>.</param>
    /// <param name="Totals">
    /// A single column-wise summary line, or empty when the payload has none. #383: separate from
    /// <paramref name="Rows"/> rather than appended to it, so the markup can put it in a table footer —
    /// a row appended to <paramref name="Rows"/> renders inside the body and reads as an entity named
    /// "Total", and every render site would have to know the last row is special.
    /// </param>
    internal sealed record PayloadTable(
        IReadOnlyList<string> Headers,
        IReadOnlyList<IReadOnlyList<string>> Rows,
        IReadOnlyList<string> Totals);

    /// <summary>
    /// The payload detail rendered as a table, with no rows when the type has none. #308.
    /// </summary>
    /// <remarks>
    /// A table rather than a list (developer, 2026-09-02): every entry has the same shape — an entity or
    /// a status, then its counts — so columns line the numbers up and a bulleted sentence per row does
    /// not.
    /// <para>
    /// Only the two types whose payload holds something their body does not: `ReseedFileApplied`'s
    /// per-entity-type breakdown (the body states only the totals) and `ImportReviewPending`'s
    /// per-status counts (the body states only the sum). The other four were measured against their own
    /// body templates and add nothing — `WhatsNew`'s payload has no properties at all.
    /// </para>
    /// <para>
    /// A payload that cannot be read yields no rows rather than throwing: a row written by an older
    /// build must still render its body.
    /// </para>
    /// </remarks>
    /// <param name="notification">The row being rendered.</param>
    /// <param name="text">The resolved UI strings, for the column headings.</param>
    internal static PayloadTable PayloadDetail(NotificationEntity notification, Quotinator.Api.I18nText.UI? text = null)
    {
        NotificationMetadataDto? payload =
            NotificationMetadataKinds.TryDeserialize(notification.MetadataKind.Parsed, notification.Metadata);

        return payload switch
        {
            ReseedFileAppliedMetadataDto applied => ReseedTable(applied, text),

            ImportReviewPendingMetadataDto review => new PayloadTable(
                [text?.NotificationsDetailStatusColumn ?? "Status",
                 text?.NotificationsDetailCountColumn  ?? "Count"],
                [.. review.Counts.Select(IReadOnlyList<string> (c) =>
                    [c.Status, c.Count.ToString(CultureInfo.CurrentCulture)])],
                // #383: no totals here. Two columns, and this payload's own body already states the
                // sum, so a totals line would restate the sentence directly above it rather than
                // align anything under a column.
                []),

            _ => new PayloadTable([], [], []),
        };
    }

    /// <summary>
    /// The <see cref="ReseedFileAppliedMetadataDto"/> breakdown, one row per entity type plus the
    /// totals line #383 added.
    /// </summary>
    /// <param name="applied">The payload being rendered.</param>
    /// <param name="text">The resolved UI strings, for the column headings and the totals label.</param>
    private static PayloadTable ReseedTable(ReseedFileAppliedMetadataDto applied, Quotinator.Api.I18nText.UI? text)
    {
        // Filtered once and reused for both the rows and their totals. Summing the payload a second
        // time independently would let the footer disagree with the table above it the moment the row
        // filter changes and the sum is not updated to match.
        List<ReseedEntityCountDto> counted =
            [.. applied.Counts
                // #377 (developer, 2026-09-09): every value the summary sentence states must be
                // findable in the detail — including how many arrived, which the sentence leads
                // with and the table had no column for. An entity type that arrived is reported
                // whatever became of it, so a row with Incoming and no outcomes is kept: that is
                // the case most worth noticing, not one to hide.
                //
                // Incoming alone is not the test, though, and assuming it was broke reading
                // history: #302 persisted payloads before Incoming existed, so those rows carry
                // Added/Modified and a defaulted Incoming of 0, and filtering on Incoming alone
                // made every one of them render as an empty table.
                .Where(c => c.Incoming > 0 || c.Added > 0 || c.Modified > 0
                         || c.Skipped > 0 || c.Unchanged > 0 || c.ResolvedToExisting > 0
                         || c.AlreadyReported > 0)];

        return new PayloadTable(
            [text?.NotificationsDetailEntityColumn ?? "Entity",
             text?.NotificationsDetailIncomingColumn ?? "Incoming",
             text?.NotificationsDetailAddedColumn  ?? "Added",
             text?.NotificationsDetailUpdatedColumn ?? "Updated",
             text?.NotificationsDetailSkippedColumn ?? "Skipped",
             text?.NotificationsDetailUnchangedColumn ?? "Unchanged",
             text?.NotificationsDetailResolvedToExistingColumn ?? "Resolved",
             text?.NotificationsDetailAlreadyReportedColumn ?? "Reported"],
            [.. counted.Select(IReadOnlyList<string> (c) =>
                [c.EntityType,
                 c.Incoming.ToString(CultureInfo.CurrentCulture),
                 c.Added.ToString(CultureInfo.CurrentCulture),
                 c.Modified.ToString(CultureInfo.CurrentCulture),
                 c.Skipped.ToString(CultureInfo.CurrentCulture),
                 c.Unchanged.ToString(CultureInfo.CurrentCulture),
                 c.ResolvedToExisting.ToString(CultureInfo.CurrentCulture),
                 c.AlreadyReported.ToString(CultureInfo.CurrentCulture)])],
            // #383: rendered even when there is only one entity type, where it necessarily repeats
            // that row. Whether to suppress it there is open until the rendered result has been seen
            // (developer, 2026-09-09) — a footer that comes and goes may read worse than one whose
            // shape is fixed, and that is a judgement about the effect rather than about the code.
            counted.Count == 0
                ? []
                : [text?.NotificationsDetailTotalLabel ?? "Total",
                   counted.Sum(c => c.Incoming).ToString(CultureInfo.CurrentCulture),
                   counted.Sum(c => c.Added).ToString(CultureInfo.CurrentCulture),
                   counted.Sum(c => c.Modified).ToString(CultureInfo.CurrentCulture),
                   counted.Sum(c => c.Skipped).ToString(CultureInfo.CurrentCulture),
                   counted.Sum(c => c.Unchanged).ToString(CultureInfo.CurrentCulture),
                   counted.Sum(c => c.ResolvedToExisting).ToString(CultureInfo.CurrentCulture),
                   counted.Sum(c => c.AlreadyReported).ToString(CultureInfo.CurrentCulture)]);
    }

    /// <summary>
    /// The translation key for the button that runs <paramref name="trigger"/>'s action. #308.
    /// </summary>
    /// <remarks>
    /// Found in T1: a generic "Run" says nothing about what is about to happen, and for an irreversible
    /// action that is the one thing the button has to say. Every executable trigger is listed
    /// explicitly, so a new one fails `ActionLabelFor_EachExecutableTrigger_IsNamed` rather than
    /// silently inheriting a label that describes nothing.
    /// </remarks>
    /// <param name="trigger">The trigger the row carries.</param>
    internal static string ActionLabelKeyFor(NotificationDismissTrigger trigger) => trigger switch
    {
        NotificationDismissTrigger.DatabaseReset        => "NotificationsResetActionButton",
        NotificationDismissTrigger.Reseed               => "NotificationsReseedActionButton",
        NotificationDismissTrigger.ImportReviewResolved => "NotificationsReviewActionButton",
        _ => "NotificationsRunActionButton",
    };

    /// <summary>
    /// The outcomes <paramref name="trigger"/>'s action can be run with, empty for a single-outcome
    /// action. #308.
    /// </summary>
    /// <remarks>
    /// An import review has two, and hiding them behind a generic button meant the operator had to
    /// click to discover what the choices even were. A single-outcome action returns none rather than
    /// one — a control offering a single option is a button, not a choice.
    /// </remarks>
    /// <param name="trigger">The trigger the row carries.</param>
    internal static IReadOnlyList<FieldResolutionChoice> ChoicesFor(NotificationDismissTrigger trigger) =>
        trigger is NotificationDismissTrigger.ImportReviewResolved
            ? [FieldResolutionChoice.Keep, FieldResolutionChoice.Replace]
            : [];

    /// <summary>The class the body cell carries, and the stylesheet targets. #308.</summary>
    internal const string BodyCellClass = "notification-body";

    /// <summary>
    /// Whether <paramref name="notification"/> renders a title element. #308.
    /// </summary>
    /// <remarks>
    /// Whitespace counts as absent, not just <see langword="null"/> and empty: a title of spaces would
    /// render as a blank line above the body, which reads as a layout fault rather than as a row with
    /// no headline. <c>Title</c> is nullable in #312's schema, so the absent case is a real shape and
    /// not a defensive check.
    /// </remarks>
    /// <param name="notification">The row being rendered.</param>
    internal static bool ShowsTitle(NotificationEntity notification) =>
        !string.IsNullOrWhiteSpace(notification.Title);

    /// <summary>
    /// Classifies a notification's display status (#278). What has already happened to a row outranks
    /// everything else: <see cref="NotificationDisplayStatus.Dismissed"/> and its reason-derived variants
    /// first, then <see cref="NotificationDisplayStatus.Expired"/>, then
    /// <see cref="NotificationDisplayStatus.Executing"/> (#367), then
    /// <see cref="NotificationDisplayStatus.ActionUnavailable"/> (#369), and only then
    /// <see cref="NotificationDisplayStatus.Active"/>.
    /// </summary>
    /// <remarks>
    /// Without the two flags, "Active" mirrors <c>Sql.Notifications.SelectActive</c>'s own active-set
    /// definition (<c>IsDismissed = 0 AND (ExpiresAt IS NULL OR ExpiresAt > @now)</c>), so it means the
    /// same thing as the startup modals' own active set — which is why the Notifications page's filter
    /// calls it without them.
    /// </remarks>
    /// <param name="notification">The row being classified.</param>
    /// <param name="now">The time expiry is judged against.</param>
    /// <param name="isExecuting">Whether this row's action is running right now.</param>
    /// <param name="actionUnavailable">Whether this row's action exists but can no longer run.</param>
    internal static NotificationDisplayStatus GetDisplayStatus(NotificationEntity notification, DateTime now, bool isExecuting = false, bool actionUnavailable = false)
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
        // #369: after everything that has already happened to the row, for the same reason Executing is.
        // What its action could still do matters only while nothing has been done yet.
        if (actionUnavailable)
            return NotificationDisplayStatus.ActionUnavailable;
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

    // #308: which row's payload detail is open in the dialog, when DetailAsDialog is set. One at a
    // time — the dialog covers the surface, so a second would be invisible behind the first.
    private Guid? DetailDialogForId;

    private string TypeLabel(NotificationType? type) => TypeLabel(type, Text);

    /// <summary>#308: the localised button label for a row's own action.</summary>
    /// <param name="notification">The row being rendered.</param>
    private string ActionLabel(NotificationEntity notification) =>
        notification.DismissTriggerKey.Parsed is NotificationDismissTrigger trigger
            ? ActionLabelKeyFor(trigger) switch
            {
                "NotificationsResetActionButton"  => Text.NotificationsResetActionButton,
                "NotificationsReseedActionButton" => Text.NotificationsReseedActionButton,
                "NotificationsReviewActionButton" => Text.NotificationsReviewActionButton,
                _                                 => Text.NotificationsRunActionButton,
            }
            : Text.NotificationsRunActionButton;

    /// <summary>#308: the localised label for how an action settled a notification.</summary>
    /// <param name="resolution">The recorded resolution.</param>
    private string ResolutionLabel(NotificationResolution resolution) => resolution switch
    {
        NotificationResolution.KeptExisting => Text.NotificationResolutionKeptExisting,
        NotificationResolution.TookIncoming => Text.NotificationResolutionTookIncoming,
        NotificationResolution.Reseeded     => Text.NotificationResolutionReseeded,
        NotificationResolution.Reset        => Text.NotificationResolutionReset,
        _ => resolution.ToString(),
    };

    private bool CanExecuteAction(NotificationEntity notification) =>
        ShowsRunControl(
            notification,
            executorCanRun: ExecutorCanRun(ActionExecutor, notification, Availability),
            isExecuting: Executing.IsExecuting(notification.Id));

    private bool ActionIsUnavailable(NotificationEntity notification) =>
        ActionIsUnavailable(ActionExecutor, notification, Availability);

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
