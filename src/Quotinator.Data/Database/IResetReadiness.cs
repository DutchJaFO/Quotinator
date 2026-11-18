using Quotinator.Data.Enums;

namespace Quotinator.Data.Database;

/// <summary>
/// Whether a Reset could set the database file aside and replace it, asked before anything moves (#457). A degraded
/// reason reads it so it never offers a Reset that would be refused: at startup and when a check after startup finds
/// the file unsound, alike.
/// </summary>
public interface IResetReadiness
{
    /// <summary>
    /// What would stop a Reset setting the database file aside, or <see langword="null"/> when nothing would. It
    /// creates the reserved folder and writes a probe file in it and in the data directory, as a Reset would, since
    /// writing is the only way to know.
    /// </summary>
    ResetObstacle? CheckSetAsideReadiness();
}
