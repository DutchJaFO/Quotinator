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
        var now = DateTime.UtcNow;
        var notification = Build(isDismissed: false, expiresAt: null);

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Active, NotificationTable.GetDisplayStatus(notification, now));
    }

    [TestMethod]
    public void GetDisplayStatus_NotDismissedFutureExpiry_IsActive()
    {
        var now = DateTime.UtcNow;
        var notification = Build(isDismissed: false, expiresAt: now.AddHours(1));

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Active, NotificationTable.GetDisplayStatus(notification, now));
    }

    [TestMethod]
    public void GetDisplayStatus_NotDismissedPastExpiry_IsExpired()
    {
        var now = DateTime.UtcNow;
        var notification = Build(isDismissed: false, expiresAt: now.AddHours(-1));

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Expired, NotificationTable.GetDisplayStatus(notification, now));
    }

    [TestMethod]
    public void GetDisplayStatus_Dismissed_IsDismissedRegardlessOfExpiry()
    {
        var now = DateTime.UtcNow;

        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Dismissed, NotificationTable.GetDisplayStatus(Build(isDismissed: true, expiresAt: null), now));
        Assert.AreEqual(NotificationTable.NotificationDisplayStatus.Dismissed, NotificationTable.GetDisplayStatus(Build(isDismissed: true, expiresAt: now.AddHours(-1)), now),
            "Dismissed must take priority over expiry — an already-dismissed row's expiry no longer matters for display.");
    }
}
