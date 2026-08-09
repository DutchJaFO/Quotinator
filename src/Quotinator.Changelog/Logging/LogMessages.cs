using Microsoft.Extensions.Logging;

namespace Quotinator.Changelog.Logging;

/// <summary>
/// Logging message templates specific to Quotinator.Changelog. See docs/logging.md's
/// "Logging call-site pattern" section for the decision procedure that governs whether a new call
/// site belongs here or in the shared <see cref="Quotinator.Logging.LogMessages"/>.
/// </summary>
internal static partial class LogMessages
{
    /// <summary>Logs falling back to the 'en' changelog when the requested language has no document.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Changelog - Resolve] Language '{Requested}' not available — falling back to 'en'")]
    public static partial void LogLanguageFallbackToEnglish(this ILogger logger, string requested);

    /// <summary>Logs successfully loading and parsing one changelog file.</summary>
    [LoggerMessage(Level = LogLevel.Debug, Message = "[Changelog - Load] Loaded {File} ({Language}, {Count} release(s))")]
    public static partial void LogChangelogFileLoaded(this ILogger logger, string file, string language, int count);

    /// <summary>Logs the total number of changelog language files loaded at startup.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Changelog - Load] {Count} language file(s) loaded: {Languages}")]
    public static partial void LogChangelogFilesLoaded(this ILogger logger, int count, string languages);
}
