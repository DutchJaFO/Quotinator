using Microsoft.AspNetCore.Diagnostics;

namespace Quotinator.Api.Middleware;

/// <summary>
/// The one list of exception types this application's own <see cref="IExceptionHandler"/>s handle and
/// log themselves. Both the handler registrations and
/// <c>ExceptionHandlerOptions.SuppressDiagnosticsCallback</c> read it, so the middleware's duplicate
/// log line is suppressed for exactly those exceptions and never for anything else (#397).
/// </summary>
internal static class DeclaredExceptionHandlers
{
    /// <summary>The exception types one of our own handlers declares, handles and logs.</summary>
    internal static IReadOnlySet<Type> ExceptionTypes => throw new NotImplementedException();

    /// <summary>
    /// Decides whether the exception-handler middleware should stay quiet about this exception:
    /// only when one of our own handler services handled it and its type is declared above.
    /// </summary>
    /// <param name="context">The middleware's own context, carrying the exception and what handled it.</param>
    /// <returns><see langword="true"/> to suppress the middleware's diagnostics for this exception.</returns>
    internal static bool ShouldSuppressDiagnostics(ExceptionHandlerSuppressDiagnosticsContext context) =>
        throw new NotImplementedException();
}
