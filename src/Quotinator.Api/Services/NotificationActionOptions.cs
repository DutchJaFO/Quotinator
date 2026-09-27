using Quotinator.Api.Enums;

namespace Quotinator.Api.Services;

/// <summary>What each <see cref="NotificationActionOption"/> does, stated once for every surface that offers it (#348).</summary>
internal static class NotificationActionOptions
{
    /// <summary>
    /// Whether running <paramref name="option"/> takes a backup: the options the caution applies to when the
    /// backups folder is at its quota. The page and the notifications response both ask this, so they
    /// cannot disagree about which options are cautioned.
    /// </summary>
    /// <param name="option">The option offered.</param>
    internal static bool TakesABackup(NotificationActionOption option) =>
        option is NotificationActionOption.BackUpThenReseed
            or NotificationActionOption.RemoveOldestBackupThenReseed
            or NotificationActionOption.ResetDatabase;
}
