namespace Quotinator.Data.Repositories;

/// <summary>
/// Tracks the last app version that completed a healthy startup (#81), so the what's-new notification
/// producer can tell which releases were missed since the previous boot.
/// </summary>
public interface IAppVersionTracker
{
    /// <summary>
    /// Returns the version recorded by the previous healthy startup, or <see langword="null"/> when
    /// none has ever been recorded — a genuinely fresh install, or a database whose
    /// <c>System_AppVersion</c> table doesn't exist yet (read before migrations run, so this is the
    /// normal state on the very first boot after this table was introduced).
    /// </summary>
    Task<string?> GetLastActiveVersionAsync();

    /// <summary>Records <paramref name="version"/> as the current version, replacing whatever was recorded before.</summary>
    Task RecordCurrentVersionAsync(string version);
}
