namespace Quotinator.Data.Repositories;

/// <summary>
/// Records which application versions have accessed this database, as an append-only history (#81,
/// reshaped by #312). Two consumers: the what's-new notification producer, which needs the version that
/// ran last to tell which releases were missed, and notification provenance, which references a specific
/// row so it stays correct after an upgrade.
/// </summary>
public interface IAppVersionTracker
{
    /// <summary>
    /// The most recently recorded entry, or <see langword="null"/> when none has ever been recorded — a
    /// genuinely fresh install, or a database whose <c>System_AppVersion</c> table does not exist yet
    /// (this is read before migrations run, so a missing table is the normal state on the first boot
    /// after the table was introduced).
    /// </summary>
    Task<AppVersionRecord?> GetLastActiveAsync();

    /// <summary>
    /// Records <paramref name="application"/>+<paramref name="version"/> as the current entry and
    /// returns its row. Append-only and idempotent: an identical pair that is already the recorded
    /// history returns the existing row rather than adding a duplicate, so a restart on the same build
    /// does not grow the table.
    /// </summary>
    Task<AppVersionRecord> RecordCurrentAsync(string application, string version);
}

/// <summary>One recorded application version, identified by the row it occupies.</summary>
/// <param name="Id">The <c>System_AppVersion</c> row id — what a notification's provenance references.</param>
/// <param name="Application">The application name, or <see langword="null"/> for rows predating #312.</param>
/// <param name="Version">The version string.</param>
public sealed record AppVersionRecord(Guid Id, string? Application, string Version);
