namespace Quotinator.Api.Enums;

/// <summary>
/// One thing a notification's action can do, offered only while it can actually run (#348).
/// <para>
/// A trigger can carry more than one: a reseed can back up first, clear room for that backup first, or
/// run without one. Naming each option lets a caller show exactly the ones that would succeed now,
/// rather than one button that then refuses.
/// </para>
/// </summary>
public enum NotificationActionOption
{
    /// <summary>Reset the database. Offered only when a backup can be taken, since a Reset refuses otherwise.</summary>
    ResetDatabase,

    /// <summary>Resolve an import review by keeping what is stored.</summary>
    KeepExisting,

    /// <summary>Resolve an import review by taking what the file brought.</summary>
    TakeIncoming,

    /// <summary>Take a backup, then reseed. Offered when a backup can be taken now.</summary>
    BackUpThenReseed,

    /// <summary>
    /// Remove the oldest backup, then take a backup and reseed. Offered when removing that one backup is
    /// what would let the backup be taken.
    /// </summary>
    RemoveOldestBackupThenReseed,

    /// <summary>
    /// Reseed with no backup, after the user has been told why and has agreed. Offered when a backup
    /// cannot be taken but the reseed itself can still complete.
    /// </summary>
    ReseedWithoutBackup,
}
