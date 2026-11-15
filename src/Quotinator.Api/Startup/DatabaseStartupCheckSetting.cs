using Quotinator.Data.Enums;

namespace Quotinator.Api.Startup;

/// <summary>
/// The <c>Quotinator:DatabaseStartupCheck</c> setting (#490, ADR 028): how thoroughly both databases are
/// checked at startup. <c>Full</c> by default; <c>Fast</c> gives up the Indexes and References layers. A Reset
/// and the integrity endpoint always check at <c>Full</c>, whatever it says.
/// </summary>
internal static class DatabaseStartupCheckSetting
{
    /// <summary>The configuration key, and the add-on's <c>database_startup_check</c> option's environment variable.</summary>
    internal const string Key = "Quotinator:DatabaseStartupCheck";

    /// <summary>The depth when the setting is absent.</summary>
    internal const DatabaseCheckDepth Default = DatabaseCheckDepth.Full;

    /// <summary>
    /// The depth <paramref name="value"/> names, case-insensitively, and whether it named one. An absent or empty
    /// value is the default and is recognised. A value that names no depth is not recognised, and checks at
    /// <see cref="Default"/>: the thorough check is the safe answer to a setting nobody can read.
    /// </summary>
    /// <param name="value">The configured value, or <see langword="null"/> when absent.</param>
    internal static (DatabaseCheckDepth Depth, bool Recognised) Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (Default, true);

        return Enum.TryParse(value.Trim(), ignoreCase: true, out DatabaseCheckDepth depth) && Enum.IsDefined(depth)
            && !int.TryParse(value, out _)
            ? (depth, true)
            : (DatabaseCheckDepth.Full, false);
    }
}
