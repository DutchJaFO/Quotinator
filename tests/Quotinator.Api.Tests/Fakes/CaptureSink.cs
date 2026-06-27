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
    /// <summary>All rendered log messages, in emission order.</summary>
    public List<string> Lines { get; } = [];

    /// <inheritdoc/>
    public void Emit(LogEvent logEvent)
        => Lines.Add(logEvent.RenderMessage());
}
