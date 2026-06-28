using Serilog.Core;
using Serilog.Events;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>
/// Serilog sink that captures rendered log messages for assertion in unit tests.
/// Uses Serilog's actual rendering pipeline so assertions match production output exactly,
/// including the effect of the {l} literal specifier that suppresses string quoting.
/// </summary>
internal sealed class CaptureSink : ILogEventSink
{
    private readonly List<(LogEventLevel Level, string Message)> _events = [];

    /// <summary>All captured events (level + rendered message), in emission order.</summary>
    public IReadOnlyList<(LogEventLevel Level, string Message)> Events => _events;

    /// <summary>Rendered messages only — convenience accessor for single-level assertions.</summary>
    public IReadOnlyList<string> Lines => _events.Select(e => e.Message).ToList();

    /// <inheritdoc/>
    public void Emit(LogEvent logEvent)
        => _events.Add((logEvent.Level, logEvent.RenderMessage()));
}
