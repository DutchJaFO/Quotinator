using Microsoft.Extensions.Logging;

namespace Quotinator.Logging;

/// <summary>
/// Shared, cross-project logging message templates whose parameter shape — not message text — recurs
/// identically across Quotinator.Data, Quotinator.Core, Quotinator.Api, and Quotinator.Changelog.
/// See docs/logging.md's "Logging call-site pattern" section for when a new call site should reuse
/// one of these versus declaring a project-local message instead.
/// </summary>
public static partial class LogMessages
{
    /// <summary>
    /// Logs a paginated query entry: subsystem tag plus the raw page/pageSize query values.
    /// <paramref name="page"/>/<paramref name="pageSize"/> deliberately carry no <c>:l</c> literal
    /// specifier — the 15 call sites this replaces never had one either (the tag was baked into the
    /// literal message text, never a template argument, so it was never quoted), and adding one now
    /// would change the rendered output from Serilog's default quoted-string form
    /// (<c>page="2" pageSize="20"</c>) to unquoted, which is not this conversion's job to fix.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="tag">The <c>[Subsystem - Phase]</c> prefix identifying the caller.</param>
    /// <param name="page">The raw, unparsed <c>page</c> query value.</param>
    /// <param name="pageSize">The raw, unparsed <c>pageSize</c> query value.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Tag:l} page={Page} pageSize={PageSize}")]
    public static partial void LogPageQuery(this ILogger logger, string tag, string? page, string? pageSize);

    /// <summary>
    /// Logs an id-keyed query entry: subsystem tag plus the requested id. <paramref name="id"/>
    /// deliberately carries no <c>:l</c> literal specifier — see <see cref="LogPageQuery"/>'s remarks.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="tag">The <c>[Subsystem - Phase]</c> prefix identifying the caller.</param>
    /// <param name="id">The requested id.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Tag:l} id={Id}")]
    public static partial void LogIdQuery(this ILogger logger, string tag, string id);

    /// <summary>
    /// Logs an exception at the moment it is thrown, before anything has had a chance to catch it, so
    /// no throw is invisible outside a debugger (#397). At this point the exception's stack trace holds
    /// only the frame that threw it — measured, not assumed — so this line identifies the throw site
    /// while the handled and not-handled lines carry the full trace.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="exception">The exception that was thrown.</param>
    public static void LogExceptionThrown(this ILogger logger, Exception exception) =>
        logger.LogExceptionThrownCore(exception, ExceptionIds.For(exception), exception.GetType().Name);

    [LoggerMessage(Level = LogLevel.Error, Message = "[Runtime - Exception] {ExceptionId:l} thrown: {ExceptionType:l}")]
    private static partial void LogExceptionThrownCore(this ILogger logger, Exception exception, string exceptionId, string exceptionType);

    /// <summary>
    /// Logs that an exception was handled at a point where a response could be formed — the line that
    /// marks it as expected behaviour rather than a fault. Carries the same id as its thrown line.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="exception">The exception that was handled.</param>
    /// <param name="reason">What the caller did with it, in a few words.</param>
    public static void LogExceptionHandled(this ILogger logger, Exception exception, string reason) =>
        logger.LogExceptionHandledCore(exception, ExceptionIds.For(exception), exception.GetType().Name, reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "[Runtime - Exception] {ExceptionId:l} handled: {ExceptionType:l} — {Reason:l}")]
    private static partial void LogExceptionHandledCore(this ILogger logger, Exception exception, string exceptionId, string exceptionType, string reason);

    /// <summary>
    /// Logs an exception no code handled — it escaped a request, a thread, or a task nobody observed.
    /// Carries the same id as its thrown line.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="exception">The exception nobody handled.</param>
    /// <param name="where">Where it escaped from, in a few words.</param>
    public static void LogExceptionNotHandled(this ILogger logger, Exception exception, string where)
    {
        // Guarded at the call site because CA1873 counts assigning an id and reading a type name as
        // work not worth doing when the level is off — see docs/logging.md's "a [LoggerMessage]
        // conversion does not, by itself, defer an expensive argument".
        if (!logger.IsEnabled(LogLevel.Critical)) return;

        logger.LogExceptionNotHandledCore(exception, ExceptionIds.For(exception), exception.GetType().Name, where);
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "[Runtime - Exception] {ExceptionId:l} not handled ({Where:l}): {ExceptionType:l}")]
    private static partial void LogExceptionNotHandledCore(this ILogger logger, Exception exception, string exceptionId, string exceptionType, string where);
}
