using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Middleware;
using Quotinator.Api.Tests.Fakes;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Quotinator.Api.Tests.Middleware;

/// <summary>
/// Which exceptions the exception-handler middleware stays quiet about (#397). Since .NET 10 it
/// suppresses its own log line for anything an <see cref="IExceptionHandler"/> reports as handled,
/// which is how <c>BadRequestExceptionHandler</c>'s <c>422</c>s came to be logged nowhere at all. The
/// rule this restores: suppress only where our own code has already logged the exception as handled.
/// </summary>
[TestClass]
public class ExceptionHandlerDiagnosticsTests
{
    /// <summary>
    /// An exception one of our handlers declared, handled and logged needs no second line from the
    /// middleware, so the callback suppresses exactly those.
    /// </summary>
    [TestMethod]
    public void DeclaredHandledException_LogsOneHandledLineAndNoMiddlewareLine()
    {
        Assert.IsNotEmpty(DeclaredExceptionHandlers.ExceptionTypes, "no handler declares anything");

        foreach (Type declared in DeclaredExceptionHandlers.ExceptionTypes)
        {
            bool suppressed = DeclaredExceptionHandlers.ShouldSuppressDiagnostics(
                Context(Instantiate(declared), ExceptionHandledType.ExceptionHandlerService));

            Assert.IsTrue(suppressed,
                $"{declared.Name} is declared and logged by us, so the middleware's own line is a duplicate");
        }
    }

    /// <summary>
    /// Anything else keeps the middleware's line — an undeclared exception, and an exception of a
    /// declared type that no handler actually handled — and the last-resort handler logs it at Critical
    /// while declining, so the middleware still produces its response.
    /// </summary>
    [TestMethod]
    public async Task UndeclaredException_KeepsTheMiddlewareLineAndLogsCritical()
    {
        Assert.IsFalse(
            DeclaredExceptionHandlers.ShouldSuppressDiagnostics(
                Context(new InvalidOperationException("nobody declared me"), ExceptionHandledType.ExceptionHandlerService)),
            "an undeclared exception was never logged by us, so suppressing the middleware would hide it entirely");

        Type declared = DeclaredExceptionHandlers.ExceptionTypes.First();
        Assert.IsFalse(
            DeclaredExceptionHandlers.ShouldSuppressDiagnostics(
                Context(Instantiate(declared), ExceptionHandledType.Unhandled)),
            "a declared type that nothing handled is an escape, not the expected path");

        (ILogger<UnhandledRequestExceptionHandler> logger, CaptureSink sink) = BuildLastResortLogger();
        UnhandledRequestExceptionHandler subject = new(logger);

        bool handled = await subject.TryHandleAsync(
            new DefaultHttpContext(),
            new InvalidOperationException("escaped the endpoint"),
            TestContext.CancellationToken);

        Assert.IsFalse(handled, "declining is what leaves the middleware to produce its 500 and its own line");
        Assert.HasCount(1, sink.Events);
        Assert.AreEqual(LogEventLevel.Fatal, sink.Events[0].Level,
            "nothing handled it, so it is Critical rather than merely an Error");
    }

    /// <summary>
    /// The suppression list and the handler registrations are one list. Two copies would drift, and the
    /// drift is invisible: a handler registered but not declared silently logs nothing at all.
    /// </summary>
    [TestMethod]
    public void SuppressionCallback_ReadsTheSameListTheHandlersAreRegisteredFrom()
    {
        string program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Quotinator.Api", "Program.cs"));

        Assert.Contains($"{nameof(DeclaredExceptionHandlers)}.{nameof(DeclaredExceptionHandlers.ShouldSuppressDiagnostics)}", program,
            "Program.cs must install the callback, or .NET 10 suppresses every handled exception's line by default");

        List<string> registered = [.. RegisteredExceptionHandlers(program)];
        List<string> declared   = [.. DeclaredExceptionHandlers.Handlers.Values.Select(h => h.Name)];

        foreach (string handler in registered.Where(h => h != nameof(UnhandledRequestExceptionHandler)))
        {
            Assert.Contains(handler, declared,
                $"{handler} is registered but declares no exception type, so nothing logs what it handles");
        }

        foreach (string handler in declared)
        {
            Assert.Contains(handler, registered,
                $"{handler} declares an exception type but is never registered, so its declaration silences a line nothing replaces");
        }
    }

    /// <summary>
    /// Builds an instance of a declared exception type. Not every one has a parameterless constructor —
    /// <see cref="BadHttpRequestException"/> does not — so a bare <c>Activator.CreateInstance</c> would
    /// fail for a reason that has nothing to do with the behaviour under test.
    /// </summary>
    private static Exception Instantiate(Type exceptionType) =>
        (Exception)(exceptionType.GetConstructor(Type.EmptyTypes) is not null
            ? Activator.CreateInstance(exceptionType)!
            : Activator.CreateInstance(exceptionType, "declared, for this test")!);

    private static IEnumerable<string> RegisteredExceptionHandlers(string program) =>
        program
            .Split("AddExceptionHandler<", StringSplitOptions.None)
            .Skip(1)
            .Select(fragment => fragment.Split('>')[0].Trim());

    private static (ILogger<UnhandledRequestExceptionHandler> Logger, CaptureSink Sink) BuildLastResortLogger()
    {
        CaptureSink sink = new();
        Serilog.Core.Logger serilog = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Error)
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (new SerilogLoggerFactory(serilog).CreateLogger<UnhandledRequestExceptionHandler>(), sink);
    }

    private static ExceptionHandlerSuppressDiagnosticsContext Context(Exception exception, ExceptionHandledType handledBy) =>
        new() { HttpContext = new DefaultHttpContext(), Exception = exception, ExceptionHandledBy = handledBy };

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>MSTest's per-test context, used for the cancellation token the handler signature takes.</summary>
    public TestContext TestContext { get; set; } = default!;
}
