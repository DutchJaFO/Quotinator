using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Quotinator.Api.Startup;

/// <summary>
/// Wires <see cref="ExceptionLogger"/> to the runtime's own exception events, once per process
/// however many hosts are built — a test host is built per test, and a second subscription would log
/// every exception twice (#397).
/// </summary>
internal static class ExceptionLogging
{
    private const string Category = "Quotinator.Api.Startup.ExceptionLogging";

    private static readonly object Gate = new();
    private static bool _subscribed;

    /// <summary>
    /// Subscribes the exception handlers, and makes sure a logger exists to write to before the host
    /// has been built. Called as the first statement of <c>Program.cs</c>; does nothing on a second
    /// call.
    /// </summary>
    internal static void Subscribe()
    {
        lock (Gate)
        {
            if (_subscribed) return;
            _subscribed = true;

            // Serilog's own CreateBootstrapLogger is deliberately not used: its reloadable logger
            // freezes when the first host is built and throws "The logger is already frozen" on the
            // next (serilog/serilog-aspnetcore#312), and Quotinator.Api.Tests builds a host per test
            // in one process. A plain logger has no such state. UseSerilog replaces Log.Logger once
            // the host exists, and the handlers below read it per call, so they follow it across.
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(LogEventLevel.Error)
                .WriteTo.Console(outputTemplate: LogOutputTemplates.Production)
                .CreateLogger();

            // Created outside DI because it has to exist before the container does, per CLAUDE.md's
            // DI policy. A factory per exception costs two small allocations, which is the right
            // trade for reading whichever logger is current — and under this project's own rule an
            // exception is never routine, so this is not a hot path.
            ExceptionLogger handler = new(
                () => new SerilogLoggerFactory(Log.Logger).CreateLogger(Category),
                Log.CloseAndFlush);

            AppDomain.CurrentDomain.FirstChanceException += handler.OnFirstChanceException;
            AppDomain.CurrentDomain.UnhandledException += handler.OnUnhandledException;
            TaskScheduler.UnobservedTaskException += handler.OnUnobservedTaskException;
        }
    }
}
