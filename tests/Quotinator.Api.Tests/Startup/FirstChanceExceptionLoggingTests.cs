using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Startup;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// Every exception is logged the moment it is thrown, whether or not anything catches it (#397).
/// <para>
/// The handlers are driven directly with constructed event arguments rather than by subscribing to
/// <see cref="AppDomain.FirstChanceException"/>: that event is process-wide, and
/// <c>docs/testing-policy.md</c> permits writing global state only from <c>[AssemblyInitialize]</c>.
/// </para>
/// <para>
/// Asserted through a real Serilog pipeline via <see cref="CaptureSink"/>, per <c>docs/logging.md</c> —
/// a MEL test double does not apply Serilog's string quoting, so it cannot prove a <c>{:l}</c>
/// specifier is present.
/// </para>
/// </summary>
[TestClass]
public class FirstChanceExceptionLoggingTests
{
    /// <summary>Matches the id the lines carry, without depending on how it is generated.</summary>
    private static readonly Regex IdPattern = new(@"\[Runtime - Exception\] (?<id>[0-9a-f]{8})", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>A thrown exception produces one Error line carrying its id, type and the exception itself.</summary>
    [TestMethod]
    public void ThrownException_IsLoggedAtErrorWithIdAndException()
    {
        (Microsoft.Extensions.Logging.ILogger logger, CaptureSink sink) = Build();
        ExceptionLogger subject = new(() => logger, () => { });
        InvalidOperationException exception = new("expected, and logged anyway");

        subject.OnFirstChanceException(null, new FirstChanceExceptionEventArgs(exception));

        Assert.HasCount(1, sink.Events);
        Assert.AreEqual(LogEventLevel.Error, sink.Events[0].Level,
            "an exception is never normal, so it is never logged below Error (developer decision, 2026-09-16)");
        Assert.Contains("[Runtime - Exception]", sink.Lines[0]);
        Assert.Contains(nameof(InvalidOperationException), sink.Lines[0]);
        Assert.IsTrue(IdPattern.IsMatch(sink.Lines[0]), $"no 8-character id in: {sink.Lines[0]}");
    }

    /// <summary>
    /// Nothing is logged while no exception is thrown, and exactly one line appears per exception that
    /// is — the second half is what makes this more than a test a silent implementation would also pass.
    /// </summary>
    [TestMethod]
    public void NoExceptionThrown_LogsNothing()
    {
        (Microsoft.Extensions.Logging.ILogger logger, CaptureSink sink) = Build();
        ExceptionLogger subject = new(() => logger, () => { });

        Assert.IsEmpty(sink.Events, "constructing the logger logs nothing by itself");

        subject.OnFirstChanceException(null, new FirstChanceExceptionEventArgs(new InvalidOperationException("one")));

        Assert.HasCount(1, sink.Events, "one thrown exception is one line — never two");
    }

    /// <summary>
    /// A failure while logging is swallowed, and the handler does not re-enter itself. Microsoft's
    /// documentation is explicit that an exception escaping a first-chance handler raises the event
    /// again, "which could result in a stack overflow and termination of the application".
    /// </summary>
    [TestMethod]
    public void LoggingItselfThrows_DoesNotRecurseOrPropagate()
    {
        ThrowingSink sink = new();
        Serilog.Core.Logger serilog = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Error)
            .WriteTo.Sink(sink)
            .CreateLogger();
        Microsoft.Extensions.Logging.ILogger logger = new SerilogLoggerFactory(serilog).CreateLogger("ExceptionLogging");
        ExceptionLogger subject = new(() => logger, () => { });

        subject.OnFirstChanceException(null, new FirstChanceExceptionEventArgs(new InvalidOperationException("boom")));

        Assert.AreEqual(1, sink.Attempts,
            "the sink's own failure must not come back through the handler as another exception to log");
    }

    /// <summary>
    /// The handled line carries the same id as the thrown line, which is what lets a reader pair them
    /// and tell expected behaviour from an exception nothing ever accounted for.
    /// </summary>
    [TestMethod]
    public void LogExceptionHandled_CarriesTheSameIdAsTheThrownLine()
    {
        (Microsoft.Extensions.Logging.ILogger logger, CaptureSink sink) = Build();
        ExceptionLogger subject = new(() => logger, () => { });
        InvalidOperationException exception = new("caught where a response could be formed");

        subject.OnFirstChanceException(null, new FirstChanceExceptionEventArgs(exception));
        logger.LogExceptionHandled(exception, "returned 422");

        Assert.HasCount(2, sink.Events);
        Assert.AreEqual(LogEventLevel.Error, sink.Events[1].Level);
        Assert.AreEqual(
            IdPattern.Match(sink.Lines[0]).Groups["id"].Value,
            IdPattern.Match(sink.Lines[1]).Groups["id"].Value,
            "both lines describe one exception, so both must carry one id");
    }

    /// <summary>
    /// Subscription happens before the host builder exists, so an exception thrown while startup is
    /// being configured is logged too. Source-scanning because the subject is the *order* of two
    /// statements in <c>Program.cs</c>, which compiled output cannot show.
    /// </summary>
    [TestMethod]
    public void ProgramCs_RegistersExceptionLoggingBeforeTheBuilderIsCreated()
    {
        string program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Quotinator.Api", "Program.cs"));

        int subscribe = program.IndexOf($"{nameof(ExceptionLogging)}.{nameof(ExceptionLogging.Subscribe)}()", StringComparison.Ordinal);
        int builder   = program.IndexOf("WebApplication.CreateBuilder", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, subscribe, "Program.cs never subscribes the exception handlers");
        Assert.IsGreaterThanOrEqualTo(0, builder, "Program.cs no longer creates a builder — this test needs updating");
        Assert.IsLessThan(builder, subscribe,
            "subscription must precede the builder, or an exception thrown while configuring startup is never logged");
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

    /// <summary>A sink that fails the way a broken console or full disk would, and counts its attempts.</summary>
    private sealed class ThrowingSink : ILogEventSink
    {
        public int Attempts { get; private set; }

        public void Emit(LogEvent logEvent)
        {
            Attempts++;
            throw new IOException("the sink itself failed");
        }
    }
}
