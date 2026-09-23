using Quotinator.Api.Components.Controls;
using Quotinator.Api.Enums;
using Quotinator.Api.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Notifications;

namespace Quotinator.Api.Tests.Components;

/// <summary>
/// Exercises <see cref="NotificationTable.TypeLabel(NotificationType?, Quotinator.Api.I18nText.UI)"/>,
/// <see cref="NotificationTable.BadgeClass"/>, and <see cref="NotificationTable.GetDisplayStatus"/>
/// (#278) — the label/badge/status mapping shared by <c>NotificationSummary</c> (the startup-modal
/// summary) and <c>Notifications</c> (the full history page, including its Status filter). This
/// project has no Blazor component-rendering test infrastructure (no bUnit), so these pure mapping
/// methods are unit-tested directly rather than via a rendered component.
/// </summary>
[TestClass]
public class NotificationTableTests
{
    private static readonly Quotinator.Api.I18nText.UI Text = new()
    {
        NotificationTypeInformation   = "Information",
        NotificationTypeWarning       = "Warning",
        NotificationTypeError         = "Error",
        NotificationTypeSuccess       = "Success",
        NotificationTypeActionRequired = "Action required",
    };

    [TestMethod]
    [DataRow(NotificationType.Information, "Information", "bg-info")]
    [DataRow(NotificationType.Warning, "Warning", "bg-warning text-dark")]
    [DataRow(NotificationType.Error, "Error", "bg-danger")]
    [DataRow(NotificationType.Success, "Success", "bg-success")]
    [DataRow(NotificationType.ActionRequired, "Action required", "bg-primary")]
    public void TypeLabelAndBadgeClass_KnownType_ReturnExpectedMapping(NotificationType type, string expectedLabel, string expectedBadgeClass)
    {
        Assert.AreEqual(expectedLabel, NotificationTable.TypeLabel(type, Text));
        Assert.AreEqual(expectedBadgeClass, NotificationTable.BadgeClass(type));
    }

    [TestMethod]
    public void TypeLabelAndBadgeClass_NullType_FallBackToPlaceholder()
    {
        Assert.AreEqual("—", NotificationTable.TypeLabel(null, Text));
        Assert.AreEqual("bg-secondary", NotificationTable.BadgeClass(null));
    }

    private static NotificationEntity Build(bool isDismissed, DateTime? expiresAt) => new()
    {
        Type        = new SafeValue<NotificationType?>(nameof(NotificationType.Information), NotificationType.Information),
        Body        = "test",
        IsDismissed = isDismissed,
        ExpiresAt   = expiresAt is DateTime dt ? SafeDateValue.From(dt) : SafeDateValue.Empty,
    };

    /// <summary>
    /// #304: a stored UTC timestamp is displayed in the host's own time zone. Found in T1, where an
    /// event logged at 16:17 local was shown on the page as 14:17.
    /// </summary>
    [TestMethod]
    public void Local_UtcValue_IsRenderedInTheHostTimeZone()
    {
        DateTime utc = new(2026, 8, 30, 14, 17, 0, DateTimeKind.Utc);

        string rendered = NotificationTable.Local(utc);

        Assert.AreEqual(utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), rendered);
    }

    /// <summary>
    /// A value read back with no <see cref="DateTimeKind"/> is still treated as UTC, which is what it
    /// is — SQLite hands back an unspecified kind, and assuming local there would leave the display
    /// correct only on a machine that happens to run in UTC.
    /// </summary>
    [TestMethod]
    public void Local_UnspecifiedKind_IsTreatedAsUtcNotLocal()
    {
        DateTime unspecified = new(2026, 8, 30, 14, 17, 0, DateTimeKind.Unspecified);
        DateTime asUtc = DateTime.SpecifyKind(unspecified, DateTimeKind.Utc);

        Assert.AreEqual(asUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), NotificationTable.Local(unspecified));
    }

    /// <summary>No timestamp renders as an em dash rather than an empty cell or a default date.</summary>
    [TestMethod]
    public void Local_Null_RendersEmDash()
        => Assert.AreEqual("—", NotificationTable.Local(null));

    /// <summary>
    /// #304: a notification whose action was carried out reads as done, not as declined. Found in T1 —
    /// running the reseed reported "Dismissed", which tells the user the opposite of what they did.
    /// </summary>
    [TestMethod]
    public void GetDisplayStatus_DismissedBecauseResolved_IsResolved()
    {
        NotificationEntity notification = Build(isDismissed: true, expiresAt: null);
        notification.DismissReason = new SafeValue<NotificationDismissReason?>(
            NotificationDismissReason.Resolved.ToString(), NotificationDismissReason.Resolved);

        Assert.AreEqual(NotificationDisplayStatus.Resolved,
            NotificationTable.GetDisplayStatus(notification, DateTime.UtcNow));
    }

    /// <summary>
    /// #303: an alert whose import batch was removed was neither carried out nor declined. Reporting
    /// it as either misstates what happened, and the page is where that has to be readable without
    /// anyone consulting the audit trail.
    /// </summary>
    [TestMethod]
    public void GetDisplayStatus_ObsoleteReason_ReportsObsolete()
    {
        NotificationEntity notification = Build(isDismissed: true, expiresAt: null);
        notification.DismissReason = new SafeValue<NotificationDismissReason?>(
            NotificationDismissReason.Obsolete.ToString(), NotificationDismissReason.Obsolete);

        Assert.AreEqual(NotificationDisplayStatus.Obsolete,
            NotificationTable.GetDisplayStatus(notification, DateTime.UtcNow));
    }

    /// <summary>The user's own dismiss still reads as dismissed — the distinction only works if both sides hold.</summary>
    [TestMethod]
    public void GetDisplayStatus_DismissedByUser_IsDismissed()
    {
        NotificationEntity notification = Build(isDismissed: true, expiresAt: null);
        notification.DismissReason = new SafeValue<NotificationDismissReason?>(
            NotificationDismissReason.Dismissed.ToString(), NotificationDismissReason.Dismissed);

        Assert.AreEqual(NotificationDisplayStatus.Dismissed,
            NotificationTable.GetDisplayStatus(notification, DateTime.UtcNow));
    }

    /// <summary>
    /// A row dismissed before #304 added the reason column keeps the original label rather than being
    /// guessed into one bucket. Claiming such a row was "done" would invent history it does not have.
    /// </summary>
    [TestMethod]
    public void GetDisplayStatus_DismissedWithNoRecordedReason_IsDismissed()
    {
        NotificationEntity notification = Build(isDismissed: true, expiresAt: null);

        Assert.AreEqual(NotificationDisplayStatus.Dismissed,
            NotificationTable.GetDisplayStatus(notification, DateTime.UtcNow));
    }

    [TestMethod]
    public void GetDisplayStatus_NotDismissedNoExpiry_IsActive()
    {
        DateTime now = DateTime.UtcNow;
        NotificationEntity notification = Build(isDismissed: false, expiresAt: null);

        Assert.AreEqual(NotificationDisplayStatus.Active, NotificationTable.GetDisplayStatus(notification, now));
    }

    [TestMethod]
    public void GetDisplayStatus_NotDismissedFutureExpiry_IsActive()
    {
        DateTime now = DateTime.UtcNow;
        NotificationEntity notification = Build(isDismissed: false, expiresAt: now.AddHours(1));

        Assert.AreEqual(NotificationDisplayStatus.Active, NotificationTable.GetDisplayStatus(notification, now));
    }

    [TestMethod]
    public void GetDisplayStatus_NotDismissedPastExpiry_IsExpired()
    {
        DateTime now = DateTime.UtcNow;
        NotificationEntity notification = Build(isDismissed: false, expiresAt: now.AddHours(-1));

        Assert.AreEqual(NotificationDisplayStatus.Expired, NotificationTable.GetDisplayStatus(notification, now));
    }

    [TestMethod]
    public void GetDisplayStatus_Dismissed_IsDismissedRegardlessOfExpiry()
    {
        DateTime now = DateTime.UtcNow;

        Assert.AreEqual(NotificationDisplayStatus.Dismissed, NotificationTable.GetDisplayStatus(Build(isDismissed: true, expiresAt: null), now));
        Assert.AreEqual(NotificationDisplayStatus.Dismissed, NotificationTable.GetDisplayStatus(Build(isDismissed: true, expiresAt: now.AddHours(-1)), now),
            "Dismissed must take priority over expiry — an already-dismissed row's expiry no longer matters for display.");
    }

    /// <summary>
    /// #367: an action that is running says so. Without it an ~11-second reseed leaves the row reading
    /// Active with a live Run button, which reads as "the click did nothing".
    /// </summary>
    [TestMethod]
    public void GetDisplayStatus_Executing_ReportsExecuting()
    {
        DateTime now = DateTime.UtcNow;
        NotificationEntity notification = Build(isDismissed: false, expiresAt: null);

        Assert.AreEqual(NotificationDisplayStatus.Executing,
            NotificationTable.GetDisplayStatus(notification, now, isExecuting: true));

        // Positive control on the same row: without it, an implementation that reported Executing
        // unconditionally would satisfy the assertion above.
        Assert.AreEqual(NotificationDisplayStatus.Active,
            NotificationTable.GetDisplayStatus(notification, now, isExecuting: false));
    }

    /// <summary>
    /// #367: the window is real, not theoretical — an action dismisses its own notification and only
    /// then releases the registry, so a row can be both dismissed and still registered. It must read
    /// what happened to it, not what was happening a moment earlier.
    /// </summary>
    [TestMethod]
    public void GetDisplayStatus_DismissedWhileExecuting_ReportsTheDismissReason()
    {
        NotificationEntity notification = Build(isDismissed: true, expiresAt: null);
        notification.DismissReason = new SafeValue<NotificationDismissReason?>(
            NotificationDismissReason.Resolved.ToString(), NotificationDismissReason.Resolved);

        Assert.AreEqual(NotificationDisplayStatus.Resolved,
            NotificationTable.GetDisplayStatus(notification, DateTime.UtcNow, isExecuting: true));
    }

    /// <summary>#367: expiry outranks executing, the same way it outranks active.</summary>
    [TestMethod]
    public void GetDisplayStatus_ExpiredWhileExecuting_ReportsExpired()
    {
        DateTime now = DateTime.UtcNow;

        Assert.AreEqual(NotificationDisplayStatus.Expired,
            NotificationTable.GetDisplayStatus(Build(isDismissed: false, expiresAt: now.AddHours(-1)), now, isExecuting: true));
    }

    /// <summary>
    /// #367: every display status needs a label, and a status added without one renders as an empty
    /// badge. Derived from the enum rather than from a maintained list, so a future member is caught
    /// by the same test that caught this one.
    /// </summary>
    [TestMethod]
    public void EveryDisplayStatus_HasATranslationKey()
    {
        string baseline = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Quotinator.Api", "i18ntext", "UI.en-GB.json");
        Dictionary<string, string> keys =
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(baseline))!;

        foreach (NotificationDisplayStatus status
                 in Enum.GetValues<NotificationDisplayStatus>())
        {
            string key = $"Notifications{status}Label";
            Assert.IsTrue(keys.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value),
                $"{status} renders with no label — '{key}' is missing or empty in UI.en-GB.json.");
        }
    }

    /// <summary>
    /// #367: the Run control is withdrawn while the action runs, rather than refusing the click after
    /// the fact. A second session sees the same thing, which is what makes the guard legible instead of
    /// silent.
    /// </summary>
    [TestMethod]
    public void ShowsRunControl_WhileExecuting_IsFalse()
    {
        NotificationEntity notification = Build(isDismissed: false, expiresAt: null);

        Assert.IsTrue(NotificationTable.ShowsRunControl(notification, executorCanRun: true, isExecuting: false));
        Assert.IsFalse(NotificationTable.ShowsRunControl(notification, executorCanRun: true, isExecuting: true));
        Assert.IsFalse(NotificationTable.ShowsRunControl(notification, executorCanRun: false, isExecuting: false),
            "No executable action means no control, executing or not.");
    }

    /// <summary>
    /// #367, found in T1: Dismiss stayed live while the action ran, and clicking it corrupted the
    /// recorded outcome. Blazor serialises circuit events, so the click queues behind the running
    /// handler and is applied <em>after</em> the action has set <c>Resolved</c> — overwriting it with
    /// <c>Dismissed</c>. Reproduced against a container with a negative control: the same run without
    /// the click records <c>resolved</c>, with it records <c>dismissed</c>, and the reseed completes
    /// either way. A carried-out action must never read as one the user declined (#304).
    /// </summary>
    [TestMethod]
    public void ShowsDismissControl_WhileExecuting_IsFalse()
    {
        NotificationEntity notification = Build(isDismissed: false, expiresAt: null);

        Assert.IsTrue(NotificationTable.ShowsDismissControl(notification, isExecuting: false));
        Assert.IsFalse(NotificationTable.ShowsDismissControl(notification, isExecuting: true),
            "There is nothing to dismiss while the action runs, and the click would overwrite its outcome.");
        Assert.IsFalse(NotificationTable.ShowsDismissControl(Build(isDismissed: true, expiresAt: null), isExecuting: false),
            "An already-dismissed row has no dismiss control — the pre-existing rule, unchanged.");
    }

    #region #369 — an action whose volatile subject is gone

    private const string LiveBatch = "7f00000a-0000-4000-8000-00000000000b";
    private const string GoneBatch = "7f00000c-0000-4000-8000-00000000000d";

    /// <summary>An import-review alert naming <paramref name="batchId"/>, carrying its real trigger and payload.</summary>
    private static NotificationEntity ReviewAlert(string batchId) => new()
    {
        Type              = new SafeValue<NotificationType?>(nameof(NotificationType.ActionRequired), NotificationType.ActionRequired),
        Body              = "Your imported file conflicting.json was reseeded, but 1 changes need your decision before they can be applied.",
        DismissTriggerKey = new SafeValue<NotificationDismissTrigger?>(nameof(NotificationDismissTrigger.ImportReviewResolved), NotificationDismissTrigger.ImportReviewResolved),
        MetadataKind      = new SafeValue<NotificationMetadataKind?>(nameof(NotificationMetadataKind.ImportReviewPending), NotificationMetadataKind.ImportReviewPending),
        Metadata          = NotificationMetadataKinds.Serialize(new ImportReviewPendingMetadataDto
        {
            FileName     = "conflicting.json",
            Origin       = FileResourceOrigin.User,
            BatchId      = batchId,
            ReleaseState = NotificationReleaseState.NotApplicable,
        }),
    };

    /// <summary>
    /// #369: the panel asks the executor with the row's own payload and this render's availability —
    /// not with the trigger alone, which says an action is wired up and nothing about whether what it
    /// acts on still exists. A trigger-only check is exactly how the notification offered Keep/Take on a
    /// batch that was gone.
    /// </summary>
    [TestMethod]
    public void ExecutorCanRun_ImportReviewWhoseBatchIsGone_IsFalse()
    {
        NotificationActionAvailability availability = new([LiveBatch]);
        AnsweringExecutor executor = new();

        Assert.IsFalse(NotificationTable.ExecutorCanRun(executor, ReviewAlert(GoneBatch), availability),
            "The batch this alert names is gone, so its action cannot run.");
        Assert.AreEqual(GoneBatch, (executor.ReceivedMetadata as ImportReviewPendingMetadataDto)?.BatchId,
            "The row's own payload must reach the executor — it is the only thing naming the batch.");
        Assert.AreSame(availability, executor.ReceivedAvailability,
            "The availability this render read must reach the executor, not one it reads for itself.");

        Assert.IsTrue(NotificationTable.ExecutorCanRun(executor, ReviewAlert(LiveBatch), availability),
            "Positive control: the same alert naming a live batch can run. Without it, a seam that never "
            + "offered any action would pass the assertion above.");
    }

    /// <summary>
    /// #369: an alert whose action can no longer be carried out says so through its state (developer,
    /// 2026-09-10). Withdrawing the Run control alone would leave an empty Action cell, indistinguishable
    /// from a row that never had an action.
    /// </summary>
    [TestMethod]
    public void GetDisplayStatus_ImportReviewWhoseBatchIsGone_IsActionUnavailable()
    {
        DateTime now = DateTime.UtcNow;
        NotificationEntity alert = ReviewAlert(GoneBatch);

        Assert.AreEqual(NotificationDisplayStatus.ActionUnavailable,
            NotificationTable.GetDisplayStatus(alert, now, isExecuting: false, actionUnavailable: true));
        Assert.AreEqual(NotificationDisplayStatus.Active,
            NotificationTable.GetDisplayStatus(alert, now, isExecuting: false, actionUnavailable: false),
            "Positive control: the same row whose action is still possible reads Active.");
    }

    /// <summary>
    /// #369, a control on precedence: what has already happened to a row outranks whether its action
    /// could still run. A dismissed, expired or running row reports that — the ordering #367 set for
    /// Executing. It passes before this issue's change as well, which is what a control is for.
    /// </summary>
    [TestMethod]
    public void GetDisplayStatus_ActionUnavailable_YieldsToDismissedExpiredAndExecuting()
    {
        DateTime now = DateTime.UtcNow;

        Assert.AreEqual(NotificationDisplayStatus.Dismissed,
            NotificationTable.GetDisplayStatus(Build(isDismissed: true, expiresAt: null), now, isExecuting: false, actionUnavailable: true));
        Assert.AreEqual(NotificationDisplayStatus.Expired,
            NotificationTable.GetDisplayStatus(Build(isDismissed: false, expiresAt: now.AddHours(-1)), now, isExecuting: false, actionUnavailable: true));
        Assert.AreEqual(NotificationDisplayStatus.Executing,
            NotificationTable.GetDisplayStatus(Build(isDismissed: false, expiresAt: null), now, isExecuting: true, actionUnavailable: true));
    }

    /// <summary>
    /// #369: "no longer possible" is said only of an action that exists and cannot run. A row with no
    /// action at all, or one whose action can still run, reads as it always did — otherwise every
    /// informational notification would claim to have lost an action it never had.
    /// </summary>
    [TestMethod]
    public void ActionIsUnavailable_OnlyForAWiredActionThatCannotRun()
    {
        NotificationActionAvailability availability = new([LiveBatch]);
        AnsweringExecutor executor = new();

        Assert.IsTrue(NotificationTable.ActionIsUnavailable(executor, ReviewAlert(GoneBatch), availability),
            "An import-review alert whose batch is gone carries an action that can no longer run.");
        Assert.IsFalse(NotificationTable.ActionIsUnavailable(executor, ReviewAlert(LiveBatch), availability),
            "Its batch still exists, so its action is still possible.");
        Assert.IsFalse(NotificationTable.ActionIsUnavailable(executor, Build(isDismissed: false, expiresAt: null), availability),
            "A notification with no action at all has nothing to lose.");
    }

    /// <summary>
    /// Answers the three-argument capability check the way the real executor does, and records what it
    /// was handed — so a test can tell a seam that passes the row's payload from one that does not.
    /// </summary>
    private sealed class AnsweringExecutor : INotificationActionExecutor
    {
        public NotificationMetadataDto? ReceivedMetadata { get; private set; }
        public NotificationActionAvailability? ReceivedAvailability { get; private set; }

        public bool CanExecute(NotificationDismissTrigger trigger) => true;

        public bool CanExecute(NotificationDismissTrigger trigger, NotificationMetadataDto? metadata, NotificationActionAvailability availability)
        {
            ReceivedMetadata     = metadata;
            ReceivedAvailability = availability;
            return metadata is ImportReviewPendingMetadataDto review && availability.ImportBatchExists(review.BatchId);
        }

        public Task<NotificationActionAvailability> GetAvailabilityAsync() =>
            throw new NotSupportedException("The table is handed its availability; it never reads one.");

        public Task ExecuteAsync(NotificationDismissTrigger trigger, NotificationMetadataDto? metadata = null, FieldResolutionChoice? choice = null) =>
            throw new NotSupportedException("The table never runs an action itself.");
    }

    #endregion

    #region #308 — title/body layout

    private static NotificationEntity WithTitle(string? title, string? metadata = null, NotificationMetadataKind? metadataKind = null)
    {
        NotificationEntity notification = Build(isDismissed: false, expiresAt: null);
        return new NotificationEntity
        {
            Type        = notification.Type,
            Title       = title,
            Body        = notification.Body,
            Metadata    = metadata,
            // #373: previously never set, so PayloadDetail — which dispatches on it — returned an empty
            // table for every fixture, and every assertion over that table held vacuously.
            MetadataKind = new SafeValue<NotificationMetadataKind?>(metadataKind?.ToString() ?? string.Empty, metadataKind),
            IsDismissed = notification.IsDismissed,
            ExpiresAt   = notification.ExpiresAt,
        };
    }

    /// <summary>
    /// #373: which kinds render structured detail, declared once. Derived from the enum by the tests
    /// below, so a kind added later fails until it is listed here — the same guarantee
    /// <c>NotificationMetadataKinds.PayloadTypes</c> gives for payload types, applied to what each kind
    /// actually renders.
    /// </summary>
    private static readonly Dictionary<NotificationMetadataKind, bool> RendersDetail = new()
    {
        [NotificationMetadataKind.Announcement]           = false,
        [NotificationMetadataKind.SchemaVersionOvershoot] = false,
        [NotificationMetadataKind.WhatsNew]               = false,
        [NotificationMetadataKind.ReseedRecommended]      = false,
        [NotificationMetadataKind.ReseedFileApplied]      = true,
        [NotificationMetadataKind.ImportReviewPending]    = true,
    };

    /// <summary>
    /// Every kind is declared above. Without this, a new member would simply be absent from the map and
    /// the tests below would skip it silently — which is the failure this whole group exists to prevent.
    /// </summary>
    [TestMethod]
    public void EveryMetadataKind_DeclaresWhetherItRendersDetail()
    {
        foreach (NotificationMetadataKind kind in Enum.GetValues<NotificationMetadataKind>())
        {
            Assert.IsTrue(RendersDetail.ContainsKey(kind),
                $"{kind} has no declared rendering expectation. A new kind must say whether it shows detail.");
        }
    }

    /// <summary>
    /// #308: the per-kind rendering decision has exactly one source — the payload's own type, read by
    /// <see cref="NotificationTable.PayloadDetail"/>. A second, parallel declaration of the same
    /// decision is what this forbids.
    /// </summary>
    /// <remarks>
    /// `LayoutFor(kind) → NotificationLayout(BodyIsMultiLine, PayloadParts)` was such a declaration: it
    /// stated which kinds show detail and which bodies wrap, and no renderer ever read it, so its tests
    /// went red and green while the feature it described was never built. Asserted over the component's
    /// own source, because the defect is the *existence* of the second source, not any value in it.
    /// </remarks>
    [TestMethod]
    public void TheRenderingDecision_HasExactlyOneSource()
    {
        string componentDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Quotinator.Api", "Components", "Controls");
        string source = File.ReadAllText(Path.Combine(componentDir, "NotificationTable.razor.cs"));

        Assert.Contains("PayloadDetail", source,
            "The renderer's own per-kind decision is missing; the markup calls it.");
        Assert.DoesNotContain("LayoutFor", source,
            "LayoutFor declares a per-kind layout no renderer reads — the decision belongs to PayloadDetail alone.");
        Assert.DoesNotContain("BodyIsMultiLine", source,
            "BodyIsMultiLine declares per-kind line-break behaviour the stylesheet applies to every kind.");
    }

    /// <summary>
    /// #308: the live document that renders every kind on both surfaces names every kind, so a kind
    /// added later fails here until that document covers it too.
    /// </summary>
    /// <remarks>
    /// A live document cannot enumerate a C# enum, so the enum is brought to the document instead. The
    /// unit tests above prove what each kind renders; only a rendered page proves a reader sees it, and
    /// a kind nobody added to the document is a kind no page was ever checked for.
    /// </remarks>
    [TestMethod]
    public void EveryLiveNotificationKind_IsNamedInTheVariantDocument()
    {
        string document = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "docs", "automated-testing", "notifications-and-changelog",
            "14-every-kind-renders-what-its-layout-promises.md");

        Assert.IsTrue(File.Exists(document),
            $"The per-kind rendering document is missing: {Path.GetFullPath(document)}");

        string text = File.ReadAllText(document);

        foreach (NotificationMetadataKind kind in Enum.GetValues<NotificationMetadataKind>())
        {
            Assert.Contains(kind.ToString(), text,
                $"{kind} is never named in the per-kind rendering document, so no surface was checked for it.");
        }
    }

    /// <summary>
    /// The positive direction, per kind: a kind declared to render detail produces rows, and one
    /// declared not to produces none — both against its own valid payload.
    /// </summary>
    [TestMethod]
    public void EveryMetadataKind_WithItsOwnPayload_RendersWhatItDeclares()
    {
        foreach (NotificationMetadataKind kind in Enum.GetValues<NotificationMetadataKind>())
        {
            NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
                WithTitle("A headline", metadata: MetadataFor(kind), metadataKind: kind));

            if (RendersDetail[kind])
            {
                Assert.IsNotEmpty(detail.Rows, $"{kind} declares structured detail and produced none.");
                Assert.IsNotEmpty(detail.Headers, $"{kind} produced rows with no column headings.");
                foreach (IReadOnlyList<string> row in detail.Rows)
                    Assert.HasCount(detail.Headers.Count, row, $"{kind} has a row whose cells do not match its headings.");
            }
            else
            {
                Assert.IsEmpty(detail.Rows, $"{kind} declares no structured detail and produced some.");
                Assert.IsEmpty(detail.Headers, $"{kind} claims columns for a table it has no rows for.");
            }
        }
    }

    /// <summary>
    /// The negative direction, per kind: an unreadable payload renders nothing and throws nothing,
    /// whichever kind claims it. A row written by an older build must never take a page down.
    /// </summary>
    [TestMethod]
    public void EveryMetadataKind_WithAnUnreadablePayload_RendersNothingAndDoesNotThrow()
    {
        foreach (NotificationMetadataKind kind in Enum.GetValues<NotificationMetadataKind>())
        {
            NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
                WithTitle("A headline", metadata: "{ not json at all", metadataKind: kind));

            Assert.IsEmpty(detail.Rows, $"{kind} rendered rows from a payload that cannot be parsed.");
        }
    }

    /// <summary>
    /// The other negative: a notification carrying no payload at all. Distinct from an unreadable one —
    /// #279's and #289's rows predate typed metadata entirely and have none.
    /// </summary>
    [TestMethod]
    public void EveryMetadataKind_WithNoPayload_RendersNothing()
    {
        foreach (NotificationMetadataKind kind in Enum.GetValues<NotificationMetadataKind>())
        {
            NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
                WithTitle("A headline", metadata: null, metadataKind: kind));

            Assert.IsEmpty(detail.Rows, $"{kind} rendered rows for a notification with no metadata.");
        }
    }

    /// <summary>#308: a notification that has a headline gets one rendered as its own element.</summary>
    [TestMethod]
    public void ShowsTitle_WithATitle_IsTrue()
        => Assert.IsTrue(NotificationTable.ShowsTitle(WithTitle("Source file needs review")));

    /// <summary>
    /// #308: <c>Title</c> is nullable in #312's schema and the two producers that shipped before it
    /// (#279, #289) carry none, so an absent title must render nothing rather than an empty element.
    /// Whitespace counts as absent — a title of spaces would render as a blank line above the body.
    /// </summary>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void ShowsTitle_WithoutATitle_IsFalse(string? title)
        => Assert.IsFalse(NotificationTable.ShowsTitle(WithTitle(title)));

    /// <summary>
    /// Positive control for the row above. Without it, a cell that rendered nothing at all would
    /// satisfy "no title element" perfectly — the same trap `11-clean-reseed-confirmation.md`'s canary
    /// found in its own first step.
    /// </summary>
    [TestMethod]
    public void ShowsTitle_WithoutATitle_StillRendersTheBody()
    {
        NotificationEntity untitled = WithTitle(null);

        Assert.IsFalse(NotificationTable.ShowsTitle(untitled));
        Assert.IsFalse(string.IsNullOrWhiteSpace(untitled.Body),
            "A row with no title still has a body, and the body is what the operator reads.");
    }

    /// <summary>
    /// #308: the markup and the stylesheet must name the same class for the body cell.
    /// </summary>
    /// <remarks>
    /// This proves the two halves agree — never that the rule reaches the element. #303's nav entry is
    /// the standing example: the class was present the whole time the icon was missing. The rendered
    /// proof is the T2 document's computed-style assertion.
    /// </remarks>
    [TestMethod]
    public void BodyCellClass_IsDefinedInTheStylesheet()
    {
        string componentDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Quotinator.Api", "Components", "Controls");

        string markup = File.ReadAllText(Path.Combine(componentDir, "NotificationTable.razor"));
        string css    = File.ReadAllText(Path.Combine(componentDir, "NotificationTable.razor.css"));

        Assert.Contains(NotificationTable.BodyCellClass, markup,
            "The body cell must carry the class the stylesheet targets.");
        Assert.Contains($".{NotificationTable.BodyCellClass}", css,
            "The stylesheet must define a rule for it, or the class is decoration.");
        Assert.Contains("pre-line", css,
            "Line breaks are rendered by white-space: pre-line, not by markup — see step 3.");
    }

    /// <summary>
    /// #279's and #289's rows carry no metadata kind at all, so the absent case must render its body
    /// and no detail rather than throwing on the way to a layout it has none of.
    /// </summary>
    /// <remarks>
    /// Replaces `NoMetadataKind_FallsBackToADefinedLayout`, which asserted `LayoutFor(null)` was
    /// non-null — a property of a map no renderer read. `EveryMetadataKind_HasALayout` went with it:
    /// every kind having an *entry* was only ever a property of that map, and what a kind renders is
    /// asserted for real by `EveryMetadataKind_WithItsOwnPayload_RendersWhatItDeclares`.
    /// </remarks>
    [TestMethod]
    public void NoMetadataKind_RendersItsBodyAndNoDetail()
    {
        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("A headline", metadata: null, metadataKind: null));

        Assert.IsEmpty(detail.Rows, "A row with no metadata kind has no payload to show detail from.");
        Assert.IsEmpty(detail.Headers, "A table with no rows must claim no columns.");
    }

    /// <summary>
    /// #308 finding 1: every resolution reads as words, derived from the enum so a member added later
    /// fails here rather than rendering as its C# name.
    /// </summary>
    [TestMethod]
    public void EveryResolution_HasATranslationKey()
    {
        Dictionary<string, string> keys = BaselineStrings();

        foreach (NotificationResolution resolution in Enum.GetValues<NotificationResolution>())
        {
            string key = $"NotificationResolution{resolution}";
            Assert.IsTrue(keys.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value),
                $"{resolution} has no label — '{key}' is missing or empty in UI.en-GB.json.");
        }
    }

    /// <summary>
    /// #308 finding 2: a layout says which parts of the payload its type renders. The boolean the first
    /// pass delivered said only whether the body wraps, which is not a layout.
    /// </summary>
    /// <remarks>
    /// The body leads for every type, with no exception (developer, 2026-09-02) — it is the summary of
    /// the payload wherever there is one, so structured detail is never an alternative to it. A draft
    /// of this row allowed a `PayloadOnly` type; it was rejected, and this asserts the rule that
    /// replaced it: a type that renders detail must also name the columns for it, so detail can never
    /// be a bare replacement for the sentence above it.
    /// </remarks>
    [TestMethod]
    public void PayloadDetail_ForEveryKind_IsSelfDescribing()
    {
        foreach (NotificationMetadataKind kind in Enum.GetValues<NotificationMetadataKind>())
        {
            NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
                WithTitle("A headline", metadata: MetadataFor(kind), metadataKind: kind));

            Assert.AreEqual(detail.Rows.Count > 0, detail.Headers.Count > 0,
                $"{kind} has {detail.Rows.Count} row(s) and {detail.Headers.Count} column heading(s) — " +
                "a table with rows must name its columns, and one with no rows must claim none.");

            foreach (IReadOnlyList<string> row in detail.Rows)
                Assert.HasCount(detail.Headers.Count, row,
                    $"{kind} has a row whose cell count does not match its headings.");
        }
    }

    /// <summary>
    /// #308 finding 2: whether a type has structured detail *beneath* the summary is what varies —
    /// asserted over what the renderer actually produces, not over a declaration beside it.
    /// </summary>
    /// <remarks>
    /// Replaces `LayoutFor_AcrossKinds_PayloadDetailVaries`, which read the same claim off
    /// `LayoutFor`'s map: it was red while the map's arms were empty and green once two were filled in,
    /// neither state requiring a renderer to exist.
    /// </remarks>
    [TestMethod]
    public void PayloadDetail_AcrossKinds_Varies()
    {
        List<bool> hasDetail =
            [.. Enum.GetValues<NotificationMetadataKind>()
                    .Select(k => NotificationTable.PayloadDetail(
                        WithTitle("A headline", metadata: MetadataFor(k), metadataKind: k)).Rows.Count > 0)
                    .Distinct()];

        Assert.HasCount(2, hasDetail,
            "Every type renders the same way, so no per-type decision is being made — some types have " +
            "structured detail worth showing and some do not.");
    }

    /// <summary>
    /// A valid, deserializable payload per kind.
    /// <para>
    /// #373: every one of these previously omitted <c>releaseState</c>, which
    /// <c>NotificationMetadataDto</c> declares <c>required</c> — so deserialization threw,
    /// <c>TryDeserialize</c> swallowed it, and every fixture yielded an empty table. Combined with
    /// <see cref="WithTitle"/> never setting the kind, that made two independent reasons for the same
    /// vacuum, and one test that could not fail.
    /// </para>
    /// </summary>
    private static string MetadataFor(NotificationMetadataKind kind) => kind switch
    {
        NotificationMetadataKind.ReseedFileApplied =>
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"System","counts":[{"entityType":"Quote","added":2,"modified":1}]}""",
        NotificationMetadataKind.ImportReviewPending =>
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"User","batchId":"b","counts":[{"status":"Pending","count":1}]}""",
        _ => """{"releaseState":"NotApplicable"}""",
    };

    /// <summary>
    /// #373: a payload written before this issue added its fields still renders, reading the absent
    /// ones as zero. Distinct from the unreadable case below — this payload is perfectly valid, just
    /// older.
    /// </summary>
    [TestMethod]
    public void PayloadWrittenBeforeUnchangedExisted_StillRenders()
    {
        // The exact shape #302 has been persisting since it shipped: entityType, added, modified, and
        // nothing else. These rows exist on the developer's own database, so a payload change that
        // cannot read them is a regression in reading history rather than a compatibility nicety.
        const string olderPayload =
            """{"releaseState":"NotApplicable","fileName":"quotinator-curated.json","origin":"System","counts":[{"entityType":"Quote","added":13,"modified":0}]}""";

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Source file reseeded cleanly", metadata: olderPayload,
                      metadataKind: NotificationMetadataKind.ReseedFileApplied));

        Assert.IsNotEmpty(detail.Rows, "An older payload still describes real work and must still render.");
        Assert.HasCount(detail.Headers.Count, detail.Rows[0],
            "Whatever layout #373 settles on, every row still matches its headers (#308's own contract).");
    }

    /// <summary>
    /// #374: a row with nothing added or modified but something skipped by policy must still surface —
    /// before this fix, the row filter (<c>Added &gt; 0 || Modified &gt; 0</c>) dropped it entirely,
    /// hiding from the UI the exact information the underlying confirmation had just stopped hiding.
    /// </summary>
    [TestMethod]
    public void SkippedOnlyRow_StillRenders()
    {
        const string payload =
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"System","counts":[{"entityType":"Quote","added":0,"modified":0,"skipped":1}]}""";

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Source file reseeded cleanly", metadata: payload,
                      metadataKind: NotificationMetadataKind.ReseedFileApplied));

        Assert.IsNotEmpty(detail.Rows, "A skipped-only row must not be dropped from the table.");
        Assert.Contains("1", detail.Rows[0], "The skipped count must appear somewhere in the row.");
    }

    /// <summary>
    /// #378 (developer, 2026-09-08): the payload has always carried `Unchanged` (#373), and the summary
    /// sentence already states its total — the table just never showed it per entity type, hiding real
    /// information a curator reading the detail popup for a "reseeded cleanly" file would otherwise have
    /// to take on faith. Purely a display change: no new computation, `Unchanged` was already there.
    /// </summary>
    [TestMethod]
    public void UnchangedColumn_ShowsTheActualCount()
    {
        const string payload =
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"System","counts":[{"entityType":"Quote","added":5,"modified":0,"unchanged":82}]}""";

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Source file reseeded cleanly", metadata: payload,
                      metadataKind: NotificationMetadataKind.ReseedFileApplied));

        Assert.Contains("Unchanged", detail.Headers, "The table must have an Unchanged column heading.");
        Assert.Contains("82", detail.Rows[0], "The unchanged count must appear in the row, not just the summary sentence's total.");
    }

    /// <summary>
    /// #377, found by T1 (developer, 2026-09-09): the confirmation's own sentence read "…and 1 resolved
    /// back to what was already stored" above a Details table with nowhere to put it, so a reader
    /// comparing the summary against its own detail found them disagreeing.
    /// </summary>
    [TestMethod]
    public void ResolvedToExistingColumn_ShowsTheActualCount()
    {
        const string payload =
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"System","counts":[{"entityType":"Quote","incoming":30,"added":5,"modified":0,"unchanged":4,"resolvedToExisting":21}]}""";

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Source file reseeded cleanly", metadata: payload,
                      metadataKind: NotificationMetadataKind.ReseedFileApplied));

        Assert.Contains("Resolved", detail.Headers, "The table must have a column for the bucket the summary sentence names.");
        Assert.Contains("21", detail.Rows[0], "…and the count must appear in the row, not only in the sentence above it.");
    }

    /// <summary>
    /// #376: the bucket for a conflict an earlier pass already staged. Without a column of its own it
    /// would be invisible while still counting toward <c>incoming</c>, so the row's own numbers would
    /// stop adding up on screen — the same disagreement between summary and detail #377 found.
    /// </summary>
    [TestMethod]
    public void AlreadyReportedColumn_ShowsTheActualCount()
    {
        const string payload =
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"System","counts":[{"entityType":"Source","incoming":9,"added":2,"modified":0,"unchanged":4,"alreadyReported":3}]}""";

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Source file reseeded cleanly", metadata: payload,
                      metadataKind: NotificationMetadataKind.ReseedFileApplied));

        Assert.Contains("Reported", detail.Headers, "The table must have a column for the already-reported bucket.");
        Assert.Contains("3", detail.Rows[0], "…and the count must appear in the row, not be silently absorbed into incoming.");
    }

    /// <summary>
    /// #376: a row carrying <em>only</em> an already-reported count is the whole point of the bucket —
    /// a file whose every conflict is already on the review queue — and must not be dropped by the row
    /// filter. Same defect #374 fixed for a skipped-only row and #377 for a resolved-only one.
    /// </summary>
    [TestMethod]
    public void AlreadyReportedOnlyRow_StillRenders()
    {
        const string payload =
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"System","counts":[{"entityType":"Source","incoming":1,"added":0,"modified":0,"alreadyReported":1}]}""";

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Source file reseeded cleanly", metadata: payload,
                      metadataKind: NotificationMetadataKind.ReseedFileApplied));

        Assert.IsNotEmpty(detail.Rows, "An already-reported-only row must not be dropped from the table.");
    }

    /// <summary>
    /// #377: a row carrying <em>only</em> a resolved-to-existing count is real information — the file
    /// brought something that differed and it resolved back to what was stored — and must not be dropped
    /// by the row filter. The same defect #374 fixed for a skipped-only row.
    /// </summary>
    [TestMethod]
    public void ResolvedToExistingOnlyRow_StillRenders()
    {
        const string payload =
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"System","counts":[{"entityType":"Source","incoming":1,"added":0,"modified":0,"resolvedToExisting":1}]}""";

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Source file reseeded cleanly", metadata: payload,
                      metadataKind: NotificationMetadataKind.ReseedFileApplied));

        Assert.IsNotEmpty(detail.Rows, "A resolved-only row must not be dropped from the table.");
    }

    /// <summary>
    /// #377: the general guard, and the one that would have caught this without anybody looking at a
    /// screenshot. Every outcome the confirmation's own sentence states must have somewhere to appear in
    /// the table beneath it — otherwise the summary and its detail describe different things, which is
    /// what T1 found. Derived from the payload's own properties rather than a list, so a bucket added
    /// later fails here until the table is widened to hold it.
    /// </summary>
    [TestMethod]
    public void EveryOutcomeTheSummaryStates_HasAColumnInTheDetail()
    {
        const string payload =
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"System","counts":[{"entityType":"Quote","incoming":10,"added":1,"modified":2,"unchanged":3,"skipped":4,"resolvedToExisting":5}]}""";

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Source file reseeded cleanly", metadata: payload,
                      metadataKind: NotificationMetadataKind.ReseedFileApplied));

        // One distinct value per bucket, so each can only be found if its own column exists. `10` is
        // Incoming, which the sentence leads with ("N items came in") and the table had no column for
        // until the developer pointed it out on 2026-09-09.
        foreach (string expected in new[] { "10", "1", "2", "3", "4", "5" })
            Assert.Contains(expected, detail.Rows[0],
                $"The count {expected} is stated in the confirmation's summary and must be visible in its detail too.");

        Assert.HasCount(detail.Headers.Count, detail.Rows[0],
            "…and every row still matches its headers (#308's contract).");
    }

    /// <summary>
    /// #377 (developer, 2026-09-09): "we should never be missing any values in the table. Skipping
    /// values hides potential." An entity type that arrived is reported whatever became of it — a row
    /// filtered out for having no outcomes hides exactly the case worth noticing, which is content that
    /// arrived and did nothing.
    /// </summary>
    [TestMethod]
    public void RowThatArrivedButProducedNoOutcome_StillRenders()
    {
        const string payload =
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"System","counts":[{"entityType":"Quote","incoming":7,"added":0,"modified":0,"unchanged":0,"skipped":0,"resolvedToExisting":0}]}""";

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Source file reseeded cleanly", metadata: payload,
                      metadataKind: NotificationMetadataKind.ReseedFileApplied));

        Assert.IsNotEmpty(detail.Rows, "Seven items arrived and nothing is recorded as having happened to them — that is a finding, not a row to hide.");
        Assert.Contains("7", detail.Rows[0], "…and the incoming count is what says so.");
    }

    /// <summary>
    /// #378: before this fix, a row with nothing added, modified, or skipped — everything already
    /// matched — was dropped entirely by the same "states nothing" filter #374 already relaxed for
    /// Skipped. Now that Unchanged is a real column, "0 0 0 N" states something (N items already
    /// matched), so the row must render rather than vanish.
    /// </summary>
    [TestMethod]
    public void UnchangedOnlyRow_NowRenders()
    {
        const string payload =
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"System","counts":[{"entityType":"Source","added":0,"modified":0,"unchanged":38}]}""";

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Source file reseeded cleanly", metadata: payload,
                      metadataKind: NotificationMetadataKind.ReseedFileApplied));

        Assert.IsNotEmpty(detail.Rows, "An unchanged-only row must not be dropped from the table.");
        Assert.Contains("38", detail.Rows[0], "The unchanged count must appear somewhere in the row.");
    }

    /// <summary>
    /// Negative case for the row above: a row whose payload cannot be read still renders. Rows written
    /// by an older build, or with metadata this build does not recognise, must degrade to the body
    /// rather than throwing a whole page away.
    /// </summary>
    [TestMethod]
    public void UnreadablePayload_FallsBackToTheBody()
    {
        NotificationEntity notification = WithTitle("Source file needs review", metadata: "{ not json at all");

        Assert.IsEmpty(NotificationTable.PayloadDetail(notification).Rows,
            "An unreadable payload contributes no detail rows, and must not throw.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(notification.Body),
            "Positive control: the body is still there to fall back to.");
    }

    /// <summary>
    /// #308 finding 4: every executable trigger's button says what it will do. Derived from the enum,
    /// so a new executable trigger fails here rather than falling back to a generic "Run".
    /// </summary>
    [TestMethod]
    public void ActionLabelFor_EachExecutableTrigger_IsNamed()
    {
        Dictionary<string, string> keys = BaselineStrings();

        foreach (NotificationDismissTrigger trigger in (NotificationDismissTrigger[])
                 [NotificationDismissTrigger.DatabaseReset, NotificationDismissTrigger.Reseed,
                  NotificationDismissTrigger.ImportReviewResolved])
        {
            string key = NotificationTable.ActionLabelKeyFor(trigger);
            Assert.IsFalse(string.IsNullOrWhiteSpace(key), $"{trigger} has no action label key.");
            Assert.IsTrue(keys.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value),
                $"{trigger}'s label key '{key}' is missing or empty in UI.en-GB.json.");
            Assert.AreNotEqual("Run", value, $"{trigger}'s button still says 'Run', which says nothing.");
        }
    }

    /// <summary>
    /// #308 finding 4: an action with two outcomes offers both, rather than hiding them behind a
    /// generic button that has to be clicked first to discover what it does.
    /// </summary>
    [TestMethod]
    public void ImportReviewResolved_OffersBothChoicesDirectly()
    {
        IReadOnlyList<FieldResolutionChoice> choices =
            NotificationTable.ChoicesFor(NotificationDismissTrigger.ImportReviewResolved);

        Assert.HasCount(2, choices);
        Assert.Contains(FieldResolutionChoice.Keep, choices);
        Assert.Contains(FieldResolutionChoice.Replace, choices);

        Assert.IsEmpty(NotificationTable.ChoicesFor(NotificationDismissTrigger.Reseed),
            "A single-outcome action offers no choice — it would be a control with one option.");
    }

    // ── #383: the detail table carries a totals line ────────────────────────────────────────────
    //
    // #377 established the invariant Incoming == Added + Modified + Unchanged + Skipped +
    // ResolvedToExisting, and this table is the only place it can be seen. A reader with several
    // entity types had to add the columns up by eye to check it.

    /// <summary>
    /// #383 row 1. Two entity types with deliberately different figures, so a totals line that
    /// echoed one row, or summed the wrong axis, cannot pass.
    /// </summary>
    [TestMethod]
    public void PayloadDetail_ReseedFileApplied_TotalsRowSumsEveryColumn()
    {
        const string payload =
            """
            {"releaseState":"NotApplicable","fileName":"a.json","origin":"System","counts":[
              {"entityType":"Quote","incoming":97,"added":0,"modified":0,"skipped":0,"unchanged":76,"resolvedToExisting":21,"alreadyReported":0},
              {"entityType":"Source","incoming":84,"added":3,"modified":1,"skipped":2,"unchanged":73,"resolvedToExisting":0,"alreadyReported":5}]}
            """;

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Source file reseeded cleanly", metadata: payload,
                      metadataKind: NotificationMetadataKind.ReseedFileApplied));

        Assert.IsNotEmpty(detail.Totals, "The reseed breakdown must carry a totals line.");
        Assert.HasCount(detail.Headers.Count, detail.Totals,
            "The totals line has one cell per column, like every row (#308's own contract).");

        // Column 0 is the label, not a number. Every other column is the column-wise sum.
        string[] expected = ["181", "3", "1", "2", "149", "21", "5"];
        for (int column = 1; column < detail.Headers.Count; column++)
        {
            Assert.AreEqual(expected[column - 1], detail.Totals[column],
                $"Column '{detail.Headers[column]}' must total its own values across every row.");
        }
    }

    /// <summary>
    /// #383 row 2. A single entity type makes the totals line repeat its only row, and it is rendered
    /// anyway — for now. Whether to suppress it there is deliberately left open until the rendered
    /// result has been seen (developer, 2026-09-09), so this test pins today's answer and a later
    /// change to it is a visible change rather than a silent one.
    /// </summary>
    [TestMethod]
    public void PayloadDetail_ReseedFileApplied_SingleEntityType_StillCarriesTotals()
    {
        const string payload =
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"System","counts":[{"entityType":"Quote","incoming":13,"added":13}]}""";

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Source file reseeded cleanly", metadata: payload,
                      metadataKind: NotificationMetadataKind.ReseedFileApplied));

        Assert.HasCount(1, detail.Rows, "Fixture guard — one entity type, so one data row.");
        Assert.IsNotEmpty(detail.Totals, "A single-row table still carries its totals line today.");
        Assert.AreEqual("13", detail.Totals[1], "Incoming totals the single row's own value.");
    }

    /// <summary>
    /// #383 row 3. The sibling payload sharing <c>PayloadTable</c> gains nothing: its two columns are
    /// Status and Count, and its own body already states the sum, so a totals line would restate the
    /// sentence directly above it.
    /// </summary>
    [TestMethod]
    public void PayloadDetail_ImportReviewPending_HasNoTotals()
    {
        // fileName, origin and batchId are `required` on the DTO — omitting them makes the payload
        // undeserializable, and PayloadDetail then returns an empty table for a reason that has
        // nothing to do with totals. The fixture guard below is what caught that while writing this.
        const string payload =
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"System","batchId":"7f000000-0000-4000-8000-00000000000b","counts":[{"status":"Pending","count":4},{"status":"Blocked","count":2}]}""";

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Import needs review", metadata: payload,
                      metadataKind: NotificationMetadataKind.ImportReviewPending));

        Assert.IsNotEmpty(detail.Rows, "Fixture guard — the sibling payload still renders its own rows.");
        Assert.IsEmpty(detail.Totals, "Only the reseed breakdown carries totals.");
    }

    /// <summary>
    /// #383 row 4. The leading cell is a translated label, never a hardcoded string — the same rule
    /// every column heading here already follows, and the reason `PayloadDetail` takes the resolved
    /// text table at all.
    /// </summary>
    [TestMethod]
    public void PayloadDetail_ReseedFileApplied_TotalsLabelComesFromTranslations()
    {
        const string payload =
            """{"releaseState":"NotApplicable","fileName":"a.json","origin":"System","counts":[{"entityType":"Quote","incoming":1,"added":1}]}""";

        NotificationTable.PayloadTable detail = NotificationTable.PayloadDetail(
            WithTitle("Source file reseeded cleanly", metadata: payload,
                      metadataKind: NotificationMetadataKind.ReseedFileApplied));

        Assert.AreEqual(BaselineStrings()["NotificationsDetailTotalLabel"], detail.Totals[0],
            "The label is whatever the English baseline says it is, so a translation change moves it.");
    }

    private static Dictionary<string, string> BaselineStrings()
    {
        string baseline = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Quotinator.Api", "i18ntext", "UI.en-GB.json");
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(baseline))!;
    }

    #endregion
}
