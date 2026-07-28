using Microsoft.Extensions.Logging;

namespace Quotinator.Data.Import;

/// <summary>Startup warnings for deprecated configuration keys.</summary>
public static class LegacyConfigWarnings
{
    /// <summary>Logs a deprecation warning if the legacy <c>Quotinator__DataPath</c> value is still set.</summary>
    public static void WarnIfDataPathStillSet(string? legacyDataPathValue, ILogger logger)
    {
        if (!string.IsNullOrEmpty(legacyDataPathValue))
            logger.LogWarning("[Database - Init] Quotinator__DataPath is deprecated and no longer used; set Quotinator__DataDir instead");
    }
}
