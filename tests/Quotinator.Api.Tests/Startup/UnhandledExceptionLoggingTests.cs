using Microsoft.Extensions.Logging;
using Quotinator.Api.Startup;
using Quotinator.Api.Tests.Fakes;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// An exception nothing handled is logged at Critical (#397) — the line that separates danger from the
/// ordinary "thrown, then handled where a response could be formed" pair.
/// <para>
/// Driven directly with constructed event arguments: both events are process-wide, and the unhandled
/// one cannot be raised for real without terminating the test run.
/// </para>
/// </summary>
[TestClass]
public class UnhandledExceptionLoggingTests
{
    /// <summary>An exception that unwound a whole thread is Critical, and carries its id.</summary>
    [TestMethod]
    public void UnhandledException_IsLoggedAtCriticalWithItsId()
    {
        (Microsoft.Extensions.Logging.ILogger logger, CaptureSink sink) = Build();
        ExceptionLogger subject = new(() => logger, () => { });
        InvalidOperationException exception = new("nothing caught this");

        subject.OnUnhandledException(null, new UnhandledExceptionEventArgs(exception, isTerminating: true));

        Assert.HasCount(1, sink.Events);
        Assert.AreEqual(LogEventLevel.Fatal, sink.Events[0].Level,
            "Critical renders as Serilog's Fatal — nothing handled this, so it is not merely an Error");
        Assert.Contains("[Runtime - Exception]", sink.Lines[0]);
        Assert.Contains(nameof(InvalidOperationException), sink.Lines[0]);
    }

    /// <summary>
    /// The log is flushed before the handler returns, because the runtime terminates the process at
    /// that point — an unwritten Critical line is the one case where the evidence dies with the app.
    /// </summary>
    [TestMethod]
    public void UnhandledException_FlushesTheLogBeforeReturning()
    {
        (Microsoft.Extensions.Logging.ILogger logger, CaptureSink sink) = Build();
        bool flushed = false;
        ExceptionLogger subject = new(() => logger, () => flushed = true);

        subject.OnUnhandledException(null, new UnhandledExceptionEventArgs(new InvalidOperationException("last words"), isTerminating: true));

        Assert.IsTrue(flushed, "the process ends as soon as this handler returns");
        Assert.HasCount(1, sink.Events, "and the line itself is still written exactly once");
    }

    /// <summary>
    /// A faulted task nobody observed is Critical too: the process survives by default, so nothing
    /// else would ever report it.
    /// </summary>
    [TestMethod]
    public void UnobservedTaskException_IsLoggedAtCriticalWithItsId()
    {
        (Microsoft.Extensions.Logging.ILogger logger, CaptureSink sink) = Build();
        ExceptionLogger subject = new(() => logger, () => { });
        InvalidOperationException inner = new("nobody awaited me");

        subject.OnUnobservedTaskException(null, new UnobservedTaskExceptionEventArgs(new AggregateException(inner)));

        Assert.HasCount(1, sink.Events);
        Assert.AreEqual(LogEventLevel.Fatal, sink.Events[0].Level);
        Assert.Contains(nameof(InvalidOperationException), sink.Lines[0],
            "the useful exception is the one inside the AggregateException, not the wrapper");
    }

    /// <summary>
    /// Both runtime events are actually subscribed. <c>Program.cs</c> calls
    /// <see cref="ExceptionLogging.Subscribe"/>, and that method is where the wiring must be — a
    /// handler nothing subscribes is a test passing against code that never runs.
    /// </summary>
    [TestMethod]
    public void ProgramCs_SubscribesToUnhandledAndUnobservedTaskExceptions()
    {
        string apiRoot = Path.Combine(RepoRoot(), "src", "Quotinator.Api");
        string program = File.ReadAllText(Path.Combine(apiRoot, "Program.cs"));
        string wiring  = File.ReadAllText(Path.Combine(apiRoot, "Startup", "ExceptionLogging.cs"));

        Assert.Contains($"{nameof(ExceptionLogging)}.{nameof(ExceptionLogging.Subscribe)}()", program);
        Assert.Contains("UnhandledException +=", wiring);
        Assert.Contains("UnobservedTaskException +=", wiring);
        Assert.Contains("FirstChanceException +=", wiring);
    }

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static (Microsoft.Extensions.Logging.ILogger Logger, CaptureSink Sink) Build()
    {
        CaptureSink sink = new();
        Serilog.Core.Logger serilog = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Error)
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (new SerilogLoggerFactory(serilog).CreateLogger("ExceptionLogging"), sink);
    }
}
