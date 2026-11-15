using Quotinator.Data.Enums;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// Judges whether one database is sound, layer by layer from the cheapest to the most thorough (#490,
/// ADR 028). Startup, a Reset and the integrity endpoint all call it, so the three cannot disagree about the
/// same file.
/// <para>
/// Engine-neutral by design: nothing on this contract names an engine, and an engine's own diagnostic is
/// carried only as verbatim text. One implementation exists per engine, and nothing selects between engines
/// (ADR 003, design goals 2 and 3).
/// </para>
/// </summary>
public interface IDatabaseCheck
{
    /// <summary>The name the database is reported under.</summary>
    string DatabaseName { get; }

    /// <summary>
    /// Runs the check and returns what every layer found. It writes nothing, and leaves the database file
    /// and every file beside it exactly as it found them.
    /// </summary>
    /// <param name="depth">How thoroughly to check.</param>
    Task<DatabaseCheckResult> CheckAsync(DatabaseCheckDepth depth);
}
