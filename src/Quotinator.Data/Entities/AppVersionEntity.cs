using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>
/// One application version that has accessed this database. Introduced by #81 as a single upserted row
/// answering "what version ran last"; #312 made it an **append-only history** — one row per distinct
/// <see cref="Application"/>+<see cref="Version"/> pair — so a notification's provenance reference stays
/// frozen instead of re-pointing when the app upgrades. "Last active version" is therefore the most
/// recent row, not "the" row.
/// </summary>
[Table("System_AppVersion")]
public sealed class AppVersionEntity : RecordBase
{
    /// <summary>The recorded version string (e.g. <c>1.8.3</c>).</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// The application that accessed the database, kept deliberately separate from
    /// <see cref="Version"/> — never one concatenated value. <see langword="null"/> for rows written by
    /// #81's version-only tracker, which predate the concept.
    /// </summary>
    public string? Application { get; init; }

    /// <summary>
    /// Explicit recording order — the authority on which entry was written last, and the only one.
    /// <see cref="RecordBase.DateCreated"/> is stored at second resolution and so cannot separate rows
    /// written in the same second; SQLite's implicit <c>rowid</c> could, but is an implementation detail
    /// whose values are reusable and therefore not safe to build ordering on. Assigned by
    /// <c>Repositories.AppVersionTracker</c>, never by a caller.
    /// </summary>
    public long SequenceNumber { get; init; }
}
