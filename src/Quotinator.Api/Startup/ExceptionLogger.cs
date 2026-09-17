using System.Runtime.ExceptionServices;
using Quotinator.Logging;

namespace Quotinator.Api.Startup;

/// <summary>
/// Logs every exception the process throws, and logs again at Critical wherever nothing handled one
/// (#397). Written as an instance rather than static handlers so a test can drive each handler
/// directly with constructed event arguments, instead of subscribing to a process-wide event — which
/// <c>docs/testing-policy.md</c> permits only from <c>[AssemblyInitialize]</c>.
/// </summary>
/// <param name="logger">
/// Resolves the logger at the moment of logging, so these handlers move from the temporary startup
/// logger to the host's own the moment it exists.
/// </param>
/// <param name="flushLog">
/// Writes out anything buffered. Called after an unhandled exception, because the runtime terminates
/// the process as soon as that handler returns.
/// </param>
internal sealed class ExceptionLogger(Func<ILogger> logger, Action flushLog)
{
    /// <summary>
    /// Guards against the recursion Microsoft's documentation warns about: an exception thrown while
    /// handling the notification raises the event again, which "could result in a stack overflow and
    /// termination of the application". Per-thread, because two threads can throw at once and neither
    /// should silence the other.
    /// </summary>
    [ThreadStatic]
    private static bool _logging;

    /// <summary>Logs an exception as it is thrown, whether or not anything catches it.</summary>
    /// <param name="sender">Ignored — the runtime's event source.</param>
    /// <param name="args">Carries the exception that was thrown.</param>
    internal void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (_logging) return;

        _logging = true;
        try
        {
            logger().LogExceptionThrown(args.Exception);
        }
        catch
        {
            // A sink that fails must not become an exception this handler reports, which is how the
            // recursion above starts. There is nowhere left to report it to.
        }
        finally
        {
            _logging = false;
        }
    }

    /// <summary>Logs an exception that no code handled, then flushes, because the process is ending.</summary>
    /// <param name="sender">Ignored — the runtime's event source.</param>
    /// <param name="args">Carries the exception that went unhandled.</param>
    internal void OnUnhandledException(object? sender, UnhandledExceptionEventArgs args)
    {
        try
        {
            if (args.ExceptionObject is Exception exception)
                logger().LogExceptionNotHandled(exception, "thread");
        }
        catch
        {
            // As above — and here the process is terminating regardless.
        }
        finally
        {
            flushLog();
        }
    }

    /// <summary>Logs each exception inside a faulted task nobody observed.</summary>
    /// <param name="sender">Ignored — the runtime's event source.</param>
    /// <param name="args">Carries the faulted task's exception.</param>
    internal void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        try
        {
            // The useful exceptions are the ones inside the AggregateException the runtime wraps them
            // in; the wrapper's own type says nothing about what failed.
            foreach (Exception inner in args.Exception.InnerExceptions)
                logger().LogExceptionNotHandled(inner, "unobserved task");
        }
        catch
        {
            // As above.
        }
    }
}
