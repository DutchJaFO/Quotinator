using Microsoft.Extensions.Logging;

namespace Quotinator.Data.Logging;

/// <summary>
/// Logging message templates specific to Quotinator.Data's migration, backup, and manifest-planning
/// infrastructure. See docs/logging.md's "Logging call-site pattern" section for the decision
/// procedure that governs whether a new call site belongs here or in the shared
/// <see cref="Quotinator.Logging.LogMessages"/>.
/// </summary>
internal static partial class LogMessages
{
    /// <summary>Logs the legacy quotes.db filename migration starting.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Init] migrating legacy filename quotes.db → {NewName}")]
    public static partial void LogLegacyFilenameMigrationStarting(this ILogger logger, string newName);

    /// <summary>Logs moving one legacy database file (main/-wal/-shm) to its new name.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Init] moving {Src} → {Dst}")]
    public static partial void LogMovingLegacyFile(this ILogger logger, string src, string dst);

    /// <summary>Logs completion of the legacy filename migration.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Init] filename migration complete → {Path}")]
    public static partial void LogLegacyFilenameMigrationComplete(this ILogger logger, string path);

    /// <summary>Logs the start of a database backup.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Backup] backing up v{Version} → {Path}")]
    public static partial void LogBackupStarting(this ILogger logger, int version, string path);

    /// <summary>Logs completion of a database backup.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Backup] backup complete")]
    public static partial void LogBackupComplete(this ILogger logger);

    /// <summary>Logs that the schema is already fully up to date — no migration needed.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Init] schema is up to date (data v{DataVersion}, app v{AppVersion})")]
    public static partial void LogSchemaUpToDate(this ILogger logger, int dataVersion, int appVersion);

    /// <summary>Logs that a recorded schema version exceeds the app's own known migration count — e.g. after a migration squash, on a database that already applied the pre-squash migrations (#289).</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "[Database - Init] schema version overshoot detected: recorded data v{DataVersion} (known: v{DataKnown}), recorded app v{AppVersion} (known: v{AppKnown}) — schema is treated as complete, but a database Reset is recommended to true up the version bookkeeping")]
    public static partial void LogSchemaVersionOvershoot(this ILogger logger, int dataVersion, int dataKnown, int appVersion, int appKnown);

    /// <summary>Logs completion of an incremental migration pass.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Init] schema updated (data v{DataVersion}, app v{AppVersion})")]
    public static partial void LogSchemaUpdated(this ILogger logger, int dataVersion, int appVersion);

    /// <summary>Logs that a genuinely fresh database is being created directly at baseline.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Init] fresh database detected — creating schema directly at baseline (data v{DataVersion}, app v{AppVersion})...")]
    public static partial void LogCreatingSchemaAtBaseline(this ILogger logger, int dataVersion, int appVersion);

    /// <summary>Logs completion of baseline schema creation.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Init] schema created at baseline (data v{DataVersion}, app v{AppVersion})")]
    public static partial void LogSchemaCreatedAtBaseline(this ILogger logger, int dataVersion, int appVersion);

    /// <summary>Logs the start of one migration phase (Data or App), listing how many migrations are pending.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Init] applying {Count} pending {Phase} migration(s) (version {Current} → {Target})...")]
    public static partial void LogApplyingMigrationPhase(this ILogger logger, int count, string phase, int current, int target);

    /// <summary>Logs that no manifest was found and files were imported in alphabetical order instead.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Init] no manifest in {Dir} — importing {Count} JSON file(s) in alphabetical order")]
    public static partial void LogNoManifestAlphabeticalOrder(this ILogger logger, string dir, int count);

    /// <summary>Logs that files not listed in the manifest were appended to the seed order.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Init] {Count} file(s) not listed in manifest will be appended: {Files}")]
    public static partial void LogUnlistedFilesAppended(this ILogger logger, int count, string files);

    /// <summary>Logs that a live source cache refresh was updated from its download URL.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - SourceRefresh] updated {File} from {Url}")]
    public static partial void LogSourceRefreshUpdated(this ILogger logger, string file, string? url);

    /// <summary>Logs that a backup was skipped because writing it would exceed the configured backup-folder budget.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "[Database - Backup] skipping backup — would exceed the {MaxGb} GB backup storage budget ({ExistingBytes} existing + {EstimatedBytes} estimated bytes)")]
    public static partial void LogBackupSkippedBudgetExceeded(this ILogger logger, int maxGb, long existingBytes, long estimatedBytes);

    /// <summary>Logs that a backup was skipped because the volume does not have enough real free space.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "[Database - Backup] skipping backup — insufficient free disk space ({AvailableBytes} available, {EstimatedBytes} estimated bytes needed)")]
    public static partial void LogBackupSkippedInsufficientDiskSpace(this ILogger logger, long availableBytes, long estimatedBytes);

    /// <summary>Logs that the separate changelog database's schema is already fully up to date — no migration needed.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Changelog - Init] schema is up to date (v{Version})")]
    public static partial void LogChangelogSchemaUpToDate(this ILogger logger, int version);

    /// <summary>Logs completion of an incremental migration pass against the separate changelog database.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Changelog - Init] schema updated v{From} → v{To}")]
    public static partial void LogChangelogSchemaUpdated(this ILogger logger, int from, int to);

    /// <summary>Logs that the separate changelog database was genuinely empty and its schema was created directly at baseline.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Changelog - Init] schema created at baseline (v{Version})")]
    public static partial void LogChangelogSchemaCreatedAtBaseline(this ILogger logger, int version);

    /// <summary>Logs completion of a changelog content refresh — how many release/unreleased entries were imported, across how many languages.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Changelog - Import] refreshed {EntryCount} entries across {LanguageCount} language(s)")]
    public static partial void LogChangelogContentRefreshed(this ILogger logger, int entryCount, int languageCount);

    /// <summary>Logs that the changelog database's own Changelog table is missing — falling back to reading its JSON files directly, matching #293's NotificationReader precedent.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "[Changelog - Read] Changelog table missing — falling back to the JSON-backed changelog service")]
    public static partial void LogChangelogTableMissingFallingBackToFile(this ILogger logger, Exception ex);
}
