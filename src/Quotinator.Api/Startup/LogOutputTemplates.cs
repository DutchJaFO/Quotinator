namespace Quotinator.Api.Startup;

/// <summary>
/// The Serilog console output templates, in one place because two loggers use them: the temporary one
/// <see cref="ExceptionLogging"/> installs before the host exists, and the configured one
/// <c>Program.cs</c> builds afterwards. Two copies would drift, and an exception logged before the host
/// would then render differently from every line after it.
/// </summary>
internal static class LogOutputTemplates
{
    /// <summary>Development: time only, since a developer already knows what day it is.</summary>
    internal const string Development =
        "{Timestamp:HH:mm:ss} {Level:u3}: {SourceContext}[{EventId:0}] {Message}{NewLine}{Exception}";

    /// <summary>Production: full timestamp, for a supervisor log read long after the fact.</summary>
    internal const string Production =
        "{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}: {SourceContext}[{EventId:0}] {Message}{NewLine}{Exception}";
}
