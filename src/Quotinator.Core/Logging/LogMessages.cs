using Microsoft.Extensions.Logging;

namespace Quotinator.Core.Logging;

/// <summary>
/// Logging message templates specific to Quotinator.Core's seeding pipeline
/// (<see cref="Quotinator.Core.Database.QuotinatorDatabaseInitializer"/>). See docs/logging.md's
/// "Logging call-site pattern" section for the decision procedure that governs whether a new call
/// site belongs here or in the shared <see cref="Quotinator.Logging.LogMessages"/>.
/// </summary>
internal static partial class LogMessages
{
    /// <summary>Logs that a reseed was requested and how many source files will be reimported.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Seed] reseed requested — clearing all data and reimporting from {Count} source file(s)...")]
    public static partial void LogReseedRequested(this ILogger logger, int count);

    /// <summary>Logs the start of importing quotes from a single seed file.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Seed] importing {Count} quotes from {File} ({Batch})...")]
    public static partial void LogImportingQuotes(this ILogger logger, int count, string file, string batch);

    /// <summary>Logs the per-file import report line.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Seed] {File} report: {Report}")]
    public static partial void LogFileReport(this ILogger logger, string file, string report);

    /// <summary>Logs that a file's import actions were left staged awaiting manual review.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Seed] {File} left staged awaiting review — batch {BatchId}, {Count} action(s) pending a decision (GET /import/actions?batchId=<BatchId>)")]
    public static partial void LogFileStagedAwaitingReview(this ILogger logger, string file, string batchId, int count);

    /// <summary>Logs completion of the whole seeding pass.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Seed] seeding complete — {Count} file(s) processed")]
    public static partial void LogSeedingComplete(this ILogger logger, int count);

    /// <summary>Logs the summary of files left staged awaiting review after a seeding pass.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Seed] {Count} source file(s) staged awaiting review: {Files}")]
    public static partial void LogFilesStagedAwaitingReview(this ILogger logger, int count, string files);

    /// <summary>Logs completion of the genre re-seed pass.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Database - Seed] genre re-seed complete — {Count} genre rows processed")]
    public static partial void LogGenreReseedComplete(this ILogger logger, int count);

    /// <summary>Logs the final per-entity-type row-count statistics after seeding.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message =
        "[Database - Stats] {Quotes} quotes  {Sources} sources  {Characters} characters  {People} people  " +
        "{Series} series  {Universes} universes  {StageDirections} stage directions  {SoundCues} sound cues  {Conversations} conversations")]
    public static partial void LogDatabaseStats(
        this ILogger logger, int quotes, int sources, int characters, int people,
        int series, int universes, int stageDirections, int soundCues, int conversations);
}
