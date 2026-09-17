namespace Quotinator.Logging;

/// <summary>
/// Assigns a short, stable id to an exception instance so every line written about that same
/// exception — thrown, handled, or not handled at all — can be matched up in a log. The id lives
/// exactly as long as the exception it belongs to, and the exception object itself is never modified.
/// </summary>
public static class ExceptionIds
{
    /// <summary>
    /// Returns this exception's id, assigning one the first time it is asked for. The same instance
    /// always answers with the same id; two instances never share one.
    /// </summary>
    /// <param name="exception">The exception to identify.</param>
    /// <returns>An 8-character hexadecimal id.</returns>
    public static string For(Exception exception) => throw new NotImplementedException();
}
