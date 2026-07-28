namespace Quotinator.Data.Helpers;

/// <summary>
/// Strips characters a log-line forging attack depends on (CWE-117) from a value before it reaches a
/// structured log call. Any value that ultimately originates from an HTTP request (a route/query
/// parameter, a header, form data) must be sanitized this way before being passed as a log template
/// argument — an unsanitized value containing a newline lets a caller inject fake log lines. See
/// <c>RequestLoggingMiddleware</c> for the original instance of this fix (CWE-117, v1.7.1); this is the
/// same technique promoted to a shared helper so every call site uses one implementation.
/// </summary>
public static class LogSanitizer
{
    /// <summary>Replaces <c>\r</c> and <c>\n</c> with a space so <paramref name="value"/> can never span or forge a log line.</summary>
    public static string ForLog(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
