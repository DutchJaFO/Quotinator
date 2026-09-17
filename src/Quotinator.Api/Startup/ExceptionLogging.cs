namespace Quotinator.Api.Startup;

/// <summary>
/// Wires <see cref="ExceptionLogger"/> to the runtime's own exception events, once per process
/// however many hosts are built — a test host is built per test, and a second subscription would log
/// every exception twice (#397).
/// </summary>
internal static class ExceptionLogging
{
    /// <summary>
    /// Subscribes the exception handlers, and makes sure a logger exists to write to before the host
    /// has been built. Called as the first statement of <c>Program.cs</c>; does nothing on a second
    /// call.
    /// </summary>
    internal static void Subscribe() => throw new NotImplementedException();
}
