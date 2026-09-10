namespace Quotinator.Api.Enums;

/// <summary>The three ways the Notifications page can restrict which notifications are shown.</summary>
internal enum NotificationFilterMode
{
    /// <summary>Only undismissed, unexpired notifications — the page's default.</summary>
    Active,

    /// <summary>Every notification, whatever has happened to it.</summary>
    All,

    /// <summary>Only notifications past their expiry without having been dismissed.</summary>
    ExpiredOnly,
}
