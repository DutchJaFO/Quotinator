using Quotinator.Data.Enums;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Startup;

/// <summary>
/// One-time notification seeding — writes a notification exactly once across the lifetime of the
/// database, regardless of how many times the app restarts, identified by a stable dedupe key expected
/// to appear verbatim in the message. Used to announce a specific release-level change (e.g. #279's
/// breaking `operationId` renames) without spamming a new row on every startup. This is the first
/// concrete producer for #278's notification mechanism — #278 itself only built the mechanism, not any
/// real producer.
/// </summary>
internal static class NotificationSeeding
{
    /// <summary>
    /// Writes <paramref name="message"/> as a new notification unless a notification containing
    /// <paramref name="dedupeKey"/> already exists in the full history (active, expired, or dismissed).
    /// </summary>
    internal static async Task SeedOnceAsync(
        INotificationReader reader, INotificationWriter writer,
        NotificationType type, string dedupeKey, string message, NotificationDismissTrigger? trigger = null)
    {
        var history = await reader.GetPagedAsync(1, 0);
        if (history.Items.Any(n => n.Message.Contains(dedupeKey, StringComparison.Ordinal)))
            return;

        await writer.WriteAsync(type, message, dismissTrigger: trigger);
    }
}
