namespace Quotinator.Data.Database;

/// <summary>
/// The backup storage arithmetic, in one place (#349).
/// <para>
/// Every figure here was previously computed inline: <c>ExistingBackupBytes</c> and
/// <c>EffectiveQuotaPercent</c> were private to <see cref="DatabaseInitializer"/>, and the ceiling was
/// written out as a literal multiplication at three separate call sites. A status endpoint publishing
/// its own copy would have been a fourth, free to drift from the check that actually refuses a Reset —
/// so the number an operator is shown and the number a destructive action is refused on are computed
/// by the same code or not at all.
/// </para>
/// <para>
/// Deliberately free of logging and of <see cref="DatabaseOptions"/>-independent state: these are pure
/// functions so both a live initializer and a read-only reader can call them without either one
/// acquiring the other's concerns.
/// </para>
/// </summary>
public static class BackupStorageBudget
{
    /// <summary>Bytes in one gigabyte, as <see cref="DatabaseOptions.MaxBackupStorageGb"/> means it.</summary>
    public const long BytesPerGigabyte = 1_073_741_824L;

    /// <summary>The absolute ceiling in bytes, never exceeded by any caller.</summary>
    /// <param name="options">The database options carrying the configured budget.</param>
    public static long CeilingBytes(DatabaseOptions options) =>
        options.MaxBackupStorageGb * BytesPerGigabyte;

    /// <summary>
    /// The operating quota actually in force, as a percentage.
    /// <para>
    /// An out-of-range configured value is neither clamped silently nor allowed to stop the
    /// application: <paramref name="outOfRange"/> reports that the default was substituted so the
    /// caller can say so loudly. Clamping into range would leave an operator never learning their
    /// setting was ignored.
    /// </para>
    /// </summary>
    /// <param name="options">The database options carrying the configured percentage.</param>
    /// <param name="outOfRange">Set when the configured value was rejected and the default used.</param>
    public static int EffectiveQuotaPercent(DatabaseOptions options, out bool outOfRange)
    {
        int configured = options.BackupQuotaPercent;
        outOfRange = configured is <= 0 or > 100;
        return outOfRange ? DatabaseOptions.DefaultBackupQuotaPercent : configured;
    }

    /// <summary>What normal operation may use, in bytes.</summary>
    /// <param name="options">The database options carrying the budget and the quota percentage.</param>
    public static long QuotaBytes(DatabaseOptions options) =>
        CeilingBytes(options) * EffectiveQuotaPercent(options, out _) / 100L;

    /// <summary>
    /// The limit a caller is measured against: the operating quota normally, the absolute ceiling when
    /// the caller has explicitly reached into the reserve between them.
    /// </summary>
    /// <param name="options">The database options carrying the budget and the quota percentage.</param>
    /// <param name="allowReserve">Whether the caller has explicitly accepted using the reserve.</param>
    public static long LimitBytes(DatabaseOptions options, bool allowReserve) =>
        allowReserve ? CeilingBytes(options) : QuotaBytes(options);

    /// <summary>
    /// Total size of every file currently in the backups folder, in bytes. A folder that does not
    /// exist is zero used, not an error — nothing has been backed up yet.
    /// </summary>
    /// <param name="backupsPath">The backups folder.</param>
    public static long UsedBytes(string backupsPath) =>
        Directory.Exists(backupsPath)
            ? Directory.EnumerateFiles(backupsPath).Sum(f => new FileInfo(f).Length)
            : 0L;
}
