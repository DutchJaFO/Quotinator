namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL script for the <c>System_Notification</c> table (#278). Consumed by
/// <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns the version number.
/// </summary>
public static class NotificationMigrations
{
    /// <summary>
    /// Creates <c>System_Notification</c> directly under its final, domain-prefixed shape — introduced
    /// fresh after ADR 015/016 were already established, so no create-then-rename pair is needed,
    /// matching <see cref="FileResourceMigrations"/>'s own precedent. <c>Type</c> and
    /// <c>DismissTriggerKey</c> are backed by <see cref="Enums.NotificationType"/>/
    /// <see cref="Enums.NotificationDismissTrigger"/> — closed sets, so per ADR 008 both carry a
    /// matching CHECK constraint from creation (<c>DismissTriggerKey</c>'s is nullable-aware, since
    /// most notifications carry no dismiss trigger). Carries a full <c>RecordBase</c> shape per ADR 002
    /// ("RecordBase applies to all tables without exception").
    /// </summary>
    public const string CreateNotificationTable = """
        CREATE TABLE IF NOT EXISTS System_Notification (
            Id                TEXT    NOT NULL PRIMARY KEY,
            Type              TEXT    NOT NULL
                              CHECK (Type IN ('Information', 'Warning', 'Error', 'Success', 'ActionRequired')),
            Message           TEXT    NOT NULL,
            ExpiresAt         TEXT,
            IsDismissed       INTEGER NOT NULL DEFAULT 0,
            DismissedAt       TEXT,
            DismissTriggerKey TEXT
                              CHECK (DismissTriggerKey IS NULL OR DismissTriggerKey IN ('DatabaseReset')),
            DateCreated       TEXT    NOT NULL,
            DateModified      TEXT,
            DateDeleted       TEXT,
            IsDeleted         INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS IX_System_Notification_Active ON System_Notification (IsDismissed, IsDeleted, ExpiresAt);
        CREATE INDEX IF NOT EXISTS IX_System_Notification_DismissTriggerKey ON System_Notification (DismissTriggerKey);
        """;
}
