using System.Runtime.ExceptionServices;

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
    /// <summary>Logs an exception as it is thrown, whether or not anything catches it.</summary>
    /// <param name="sender">Ignored — the runtime's event source.</param>
    /// <param name="args">Carries the exception that was thrown.</param>
    internal void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
    {
        _ = logger;
        throw new NotImplementedException();
    }

    /// <summary>Logs an exception that no code handled, then flushes, because the process is ending.</summary>
    /// <param name="sender">Ignored — the runtime's event source.</param>
    /// <param name="args">Carries the exception that went unhandled.</param>
    internal void OnUnhandledException(object? sender, UnhandledExceptionEventArgs args)
    {
        _ = logger;
        _ = flushLog;
        throw new NotImplementedException();
    }

    /// <summary>Logs each exception inside a faulted task nobody observed.</summary>
    /// <param name="sender">Ignored — the runtime's event source.</param>
    /// <param name="args">Carries the faulted task's exception.</param>
    internal void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        _ = logger;
        throw new NotImplementedException();
    }
}
