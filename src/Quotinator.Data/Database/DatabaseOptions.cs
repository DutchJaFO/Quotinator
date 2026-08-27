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
    /// may grow to — the absolute ceiling, never exceeded. A hard, self-imposed limit independent of how
    /// much real disk space happens to be free. Default <c>1</c>: sized from a representative database
    /// size (~8 MB) × 10 backups = 80 MB, rounded up to a clean, convenient value. Overridable via
    /// <c>Quotinator:MaxBackupStorageGb</c>.
    /// </summary>
    public int MaxBackupStorageGb { get; init; } = 1;

    /// <summary>
    /// What share of <see cref="MaxBackupStorageGb"/> normal operation may use, as a percentage.
    /// Default <c>90</c>; overridable via <c>Quotinator:BackupQuotaPercent</c>.
    /// <para>
    /// #348: the space between this quota and the ceiling is a deliberate reserve, and it exists
    /// because a backup's size cannot be predicted. SQLite copies pages, so the source file's length
    /// only approximates what the copy will occupy — an uncheckpointed WAL, free pages and vacuum state
    /// all move it. Deciding a hard yes/no from that approximation claims a precision it does not have.
    /// </para>
    /// <para>
    /// So normal operation stops at the quota, and the reserve stays available for the moment it is most
    /// needed — an operator running a Reset who has nowhere left to put its backup. Reaching into it
    /// requires the caller to explicitly accept proceeding without a full guarantee; it is never
    /// automatic.
    /// </para>
    /// </summary>
    public int BackupQuotaPercent { get; init; } = DefaultBackupQuotaPercent;

    /// <summary>The default operating quota, as a percentage of <see cref="MaxBackupStorageGb"/>.</summary>
    public const int DefaultBackupQuotaPercent = 90;
}
