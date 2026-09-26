namespace Quotinator.Data.Database;

/// <summary>Runtime paths and settings passed to <see cref="DatabaseInitializer"/> at startup.</summary>
public sealed record DatabaseOptions
{
    /// <summary>Absolute path to the <c>.db</c> file.</summary>
    public required string DbPath { get; init; }

    /// <summary>Directory where pre-migration backups are written.</summary>
    public string BackupsPath { get; init; } = string.Empty;

    /// <summary>
    /// Maximum total size, in GB, the <see cref="BackupsPath"/> folder's own accumulated backup files
    /// may grow to: the absolute ceiling, past which a backup is refused. A hard, self-imposed limit independent of how
    /// much real disk space happens to be free. Default <c>1</c>: sized from a representative database
    /// size (~8 MB) × 10 backups = 80 MB, rounded up to a clean, convenient value. Overridable via
    /// <c>Quotinator:MaxBackupStorageGb</c>.
    /// </summary>
    public int MaxBackupStorageGb { get; init; } = 1;

    /// <summary>
    /// The share of <see cref="MaxBackupStorageGb"/> above which the user is warned, as a percentage.
    /// Default <c>90</c>; overridable via <c>Quotinator:BackupQuotaPercent</c>.
    /// <para>
    /// #348: the space between this quota and the ceiling is a deliberate reserve, and it exists
    /// because a backup's size cannot be predicted. SQLite copies pages, so the source file's length
    /// only approximates what the copy will occupy: an uncheckpointed WAL, free pages and vacuum state
    /// all move it.
    /// </para>
    /// <para>
    /// So a backup inside the reserve is still taken, and a warning notification raised, so the user can
    /// remove older backups or raise this quota before the ceiling refuses one (developer, 2026-09-26).
    /// </para>
    /// </summary>
    public int BackupQuotaPercent { get; init; } = DefaultBackupQuotaPercent;

    /// <summary>The default operating quota, as a percentage of <see cref="MaxBackupStorageGb"/>.</summary>
    public const int DefaultBackupQuotaPercent = 90;
}
