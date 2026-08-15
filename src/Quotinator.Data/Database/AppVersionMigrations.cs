namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL for the <c>System_AppVersion</c> table (#81) — tracks the last app version
/// that completed a healthy startup, read before migrations run on the following boot so the what's-new
/// notification producer can tell which releases were missed. Consumed by
/// <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns the version number.
/// </summary>
public static class AppVersionMigrations
{
    /// <summary>
    /// Creates <c>System_AppVersion</c>, directly under its final name — introduced fresh, so no
    /// create-then-rename pair is needed. Carries <c>RecordBase</c>'s columns per ADR 002.
    /// <para>
    /// As written here the table held exactly one upserted row. #312 replaced that with an append-only
    /// history — see <see cref="AppVersionHistoryMigrations"/>, which adds the <c>Application</c> column
    /// and the <c>SequenceNumber</c> counter the current shape relies on. This SQL is frozen at its
    /// original form per the never-edit-an-applied-migration rule; only this description is updated, so
    /// a reader is not left with a description of a shape that no longer exists.
    /// </para>
    /// </summary>
    public const string CreateAppVersionTable = """
        CREATE TABLE IF NOT EXISTS System_AppVersion (
            Id           TEXT NOT NULL PRIMARY KEY,
            Version      TEXT NOT NULL,
            DateCreated  TEXT NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0
        );
        """;
}
