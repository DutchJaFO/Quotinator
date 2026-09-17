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
    /// <summary>
    /// Each exception type one of our own handlers declares, handled and logged by the handler type it
    /// maps to. One list, read both by the registration in <c>Program.cs</c> and by the suppression
    /// callback — two copies would drift, and a handler registered but not declared would silently log
    /// nothing at all.
    /// </summary>
    internal static IReadOnlyDictionary<Type, Type> Handlers { get; } = new Dictionary<Type, Type>
    {
        [typeof(BadHttpRequestException)] = typeof(BadRequestExceptionHandler),
    };

    /// <summary>The exception types one of our own handlers declares, handles and logs.</summary>
    internal static IReadOnlySet<Type> ExceptionTypes { get; } = Handlers.Keys.ToHashSet();

    /// <summary>
    /// Decides whether the exception-handler middleware should stay quiet about this exception:
    /// only when one of our own handler services handled it and its type is declared above.
    /// </summary>
    /// <param name="context">The middleware's own context, carrying the exception and what handled it.</param>
    /// <returns><see langword="true"/> to suppress the middleware's diagnostics for this exception.</returns>
    internal static bool ShouldSuppressDiagnostics(ExceptionHandlerSuppressDiagnosticsContext context) =>
        context.ExceptionHandledBy == ExceptionHandledType.ExceptionHandlerService
        // IsInstanceOfType rather than an exact type match: a handler declining on `is not
        // BadHttpRequestException` also handles its subclasses, so the two checks must agree.
        && ExceptionTypes.Any(declared => declared.IsInstanceOfType(context.Exception));
}
