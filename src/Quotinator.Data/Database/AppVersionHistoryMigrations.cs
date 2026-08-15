namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL turning <c>System_AppVersion</c> into an append-only application/version
/// history (#312). Consumed by <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns the
/// version numbers.
/// <para>
/// #81 introduced the table as one row upserted in place, which answered "what version ran last" but
/// cannot support #312's provenance reference: a notification pointing at that row would silently start
/// claiming it came from a newer version the moment the app upgraded. Append-only freezes each row, and
/// as a side effect records every application version that has ever accessed this database.
/// </para>
/// <para>
/// Two migrations rather than one, per CLAUDE.md's "one schema change per migration where possible" —
/// the identity change (<see cref="AddApplicationColumn"/>) and the ordering change
/// (<see cref="AddSequenceNumberColumn"/>) are independent, and a multi-statement migration is harder to
/// reason about when partially applied.
/// </para>
/// </summary>
public static class AppVersionHistoryMigrations
{
    /// <summary>
    /// Adds the <c>Application</c> column — deliberately a column of its own, never concatenated into
    /// <c>Version</c> — and a uniqueness index over the pair, which is what makes the table an
    /// append-only history rather than a convention the writer merely follows.
    /// <para>
    /// <c>Application</c> is nullable rather than <c>NOT NULL DEFAULT '…'</c>: rows written by #81's
    /// version-only tracker genuinely predate the concept, and inventing an application name for them
    /// would be fabricating history. The unique index treats those legacy rows correctly — SQLite
    /// considers <c>NULL</c>s distinct in a <c>UNIQUE</c> index, so pre-existing rows never collide.
    /// </para>
    /// </summary>
    public const string AddApplicationColumn = """
        ALTER TABLE System_AppVersion ADD COLUMN Application TEXT;
        CREATE UNIQUE INDEX IF NOT EXISTS UX_System_AppVersion_Application_Version
            ON System_AppVersion (Application, Version);
        """;

    /// <summary>
    /// Adds <c>SequenceNumber</c>, the explicit recording-order counter that answers "which version ran
    /// last". Assigned by <c>Repositories.AppVersionTracker</c> as <c>MAX + 1</c> inside the insert's own
    /// transaction, and protected by a uniqueness index so a concurrent write fails loudly instead of
    /// producing two rows that claim the same position.
    /// <para>
    /// A dedicated column rather than an existing one, because neither alternative is trustworthy.
    /// <c>DateCreated</c> is stored at second resolution (see <c>Helpers.SafeDateHandler</c>'s formats),
    /// so every row written inside the same second is indistinguishable by timestamp. SQLite's implicit
    /// <c>rowid</c> orders correctly today but is not a stable guarantee to build on — it is an
    /// implementation detail whose values can be reused once a table's highest row is removed, so a
    /// future change to how this table is pruned would silently corrupt the ordering rather than fail.
    /// </para>
    /// <para>
    /// The backfill does read <c>rowid</c>, and that is deliberate and bounded: it runs once, at
    /// migration time, to give pre-existing rows the only insertion-order signal they carry at all.
    /// Reading it at a known-sane moment to seed a column that is authoritative from then on is a
    /// different thing from depending on it at every read.
    /// </para>
    /// <para>
    /// <c>DEFAULT 0</c> exists only to satisfy SQLite's requirement that a <c>NOT NULL</c> column added
    /// by <c>ALTER TABLE</c> carry a default. Combined with the uniqueness index it is a useful failure
    /// mode, not a usable value: a second row inserted without an explicit sequence collides at 0 and
    /// throws, rather than quietly joining the history in an undefined position.
    /// </para>
    /// </summary>
    public const string AddSequenceNumberColumn = """
        ALTER TABLE System_AppVersion ADD COLUMN SequenceNumber INTEGER NOT NULL DEFAULT 0;
        UPDATE System_AppVersion SET SequenceNumber = rowid;
        CREATE UNIQUE INDEX IF NOT EXISTS UX_System_AppVersion_SequenceNumber
            ON System_AppVersion (SequenceNumber);
        """;
}
