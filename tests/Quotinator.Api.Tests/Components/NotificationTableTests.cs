using Quotinator.Api.Components.Controls;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;

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

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Resolved,
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

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Obsolete,
            NotificationTable.GetDisplayStatus(notification, DateTime.UtcNow));
    }

    /// <summary>The user's own dismiss still reads as dismissed — the distinction only works if both sides hold.</summary>
    [TestMethod]
    public void GetDisplayStatus_DismissedByUser_IsDismissed()
    {
        NotificationEntity notification = Build(isDismissed: true, expiresAt: null);
        notification.DismissReason = new SafeValue<NotificationDismissReason?>(
            NotificationDismissReason.Dismissed.ToString(), NotificationDismissReason.Dismissed);

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Dismissed,
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

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Dismissed,
            NotificationTable.GetDisplayStatus(notification, DateTime.UtcNow));
    }

    [TestMethod]
    public void GetDisplayStatus_NotDismissedNoExpiry_IsActive()
    {
        DateTime now = DateTime.UtcNow;
        NotificationEntity notification = Build(isDismissed: false, expiresAt: null);

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Active, NotificationTable.GetDisplayStatus(notification, now));
    }

    [TestMethod]
    public void GetDisplayStatus_NotDismissedFutureExpiry_IsActive()
    {
        DateTime now = DateTime.UtcNow;
        NotificationEntity notification = Build(isDismissed: false, expiresAt: now.AddHours(1));

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Active, NotificationTable.GetDisplayStatus(notification, now));
    }

    [TestMethod]
    public void GetDisplayStatus_NotDismissedPastExpiry_IsExpired()
    {
        DateTime now = DateTime.UtcNow;
        NotificationEntity notification = Build(isDismissed: false, expiresAt: now.AddHours(-1));

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Expired, NotificationTable.GetDisplayStatus(notification, now));
    }

    [TestMethod]
    public void GetDisplayStatus_Dismissed_IsDismissedRegardlessOfExpiry()
    {
        DateTime now = DateTime.UtcNow;

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Dismissed, NotificationTable.GetDisplayStatus(Build(isDismissed: true, expiresAt: null), now));
        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Dismissed, NotificationTable.GetDisplayStatus(Build(isDismissed: true, expiresAt: now.AddHours(-1)), now),
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

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Executing,
            NotificationTable.GetDisplayStatus(notification, now, isExecuting: true));

        // Positive control on the same row: without it, an implementation that reported Executing
        // unconditionally would satisfy the assertion above.
        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Active,
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

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Resolved,
            NotificationTable.GetDisplayStatus(notification, DateTime.UtcNow, isExecuting: true));
    }

    /// <summary>#367: expiry outranks executing, the same way it outranks active.</summary>
    [TestMethod]
    public void GetDisplayStatus_ExpiredWhileExecuting_ReportsExpired()
    {
        DateTime now = DateTime.UtcNow;

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Expired,
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

        foreach (NotificationTable.NotificationDisplayStatus status
                 in Enum.GetValues<NotificationTable.NotificationDisplayStatus>())
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

    #region #308 — title/body layout

    private static NotificationEntity WithTitle(string? title, string? metadata = null)
    {
        NotificationEntity notification = Build(isDismissed: false, expiresAt: null);
        return new NotificationEntity
        {
            Type        = notification.Type,
            Title       = title,
            Body        = notification.Body,
            Metadata    = metadata,
            IsDismissed = notification.IsDismissed,
            ExpiresAt   = notification.ExpiresAt,
        };
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
    /// #308: every notification type has a layout decision. Derived from the enum rather than a
    /// maintained list, so a kind added later fails here instead of rendering unstyled.
    /// </summary>
    [TestMethod]
    public void EveryMetadataKind_HasALayout()
    {
        foreach (NotificationMetadataKind kind in Enum.GetValues<NotificationMetadataKind>())
            Assert.IsNotNull(NotificationTable.LayoutFor(kind), $"{kind} has no defined layout.");
    }

    /// <summary>
    /// The negative case for the row above: #279's and #289's rows carry no metadata kind at all, so
    /// the absent case needs a layout too rather than falling through to nothing.
    /// </summary>
    [TestMethod]
    public void NoMetadataKind_FallsBackToADefinedLayout()
        => Assert.IsNotNull(NotificationTable.LayoutFor(null),
            "A row with no metadata kind still renders, so it still needs a layout.");

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
    /// replaced it.
    /// </remarks>
    [TestMethod]
    public void ContentLines_ForEveryKind_LeadWithTheBody()
    {
        foreach (NotificationMetadataKind kind in Enum.GetValues<NotificationMetadataKind>())
        {
            NotificationEntity notification = WithTitle("A headline", metadata: MetadataFor(kind));
            IReadOnlyList<string> lines = NotificationTable.ContentLines(notification);

            Assert.IsNotEmpty(lines, $"{kind} renders nothing at all.");
            Assert.AreEqual(notification.Body, lines[0],
                $"{kind} does not lead with its body — the summary must never be replaced by its own detail.");
        }
    }

    /// <summary>
    /// #308 finding 2: whether a type has structured detail *beneath* the summary is what varies. If no
    /// type has any, `LayoutFor` is still the line-wrapping boolean the first pass delivered under a
    /// longer name.
    /// </summary>
    [TestMethod]
    public void LayoutFor_AcrossKinds_PayloadDetailVaries()
    {
        List<bool> hasDetail =
            [.. Enum.GetValues<NotificationMetadataKind>()
                    .Select(k => NotificationTable.LayoutFor(k)!.PayloadParts.Count > 0)
                    .Distinct()];

        Assert.HasCount(2, hasDetail,
            "Every type answers the same way, so no per-type decision is being made — some types have " +
            "structured detail worth showing and some do not.");
    }

    private static string MetadataFor(NotificationMetadataKind kind) => kind switch
    {
        NotificationMetadataKind.ReseedFileApplied =>
            """{"fileName":"a.json","origin":"System","counts":[{"entityType":"Quote","added":2,"modified":1}]}""",
        NotificationMetadataKind.ImportReviewPending =>
            """{"fileName":"a.json","origin":"User","batchId":"b","counts":[{"status":"Pending","count":1}]}""",
        _ => "{}",
    };

    /// <summary>
    /// Negative case for the row above: a row whose payload cannot be read still renders. Rows written
    /// by an older build, or with metadata this build does not recognise, must degrade to the body
    /// rather than throwing a whole page away.
    /// </summary>
    [TestMethod]
    public void UnreadablePayload_FallsBackToTheBody()
    {
        NotificationEntity notification = WithTitle("Source file needs review", metadata: "{ not json at all");

        Assert.IsEmpty(NotificationTable.PayloadLines(notification),
            "An unreadable payload contributes no detail lines, and must not throw.");
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

    private static Dictionary<string, string> BaselineStrings()
    {
        string baseline = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Quotinator.Api", "i18ntext", "UI.en-GB.json");
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(baseline))!;
    }

    #endregion
}
