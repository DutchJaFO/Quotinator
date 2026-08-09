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
}
