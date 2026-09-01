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

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Executing,
            NotificationTable.GetDisplayStatus(Build(isDismissed: false, expiresAt: null), now, isExecuting: true));
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
}
