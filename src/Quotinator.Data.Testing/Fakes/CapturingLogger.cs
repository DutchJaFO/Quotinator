using Microsoft.Extensions.Logging;

namespace Quotinator.Data.Testing.Fakes;

/// <summary>
/// Captures every rendered log message for a test to assert against, instead of the message going to
/// <see cref="Microsoft.Extensions.Logging.Abstractions.NullLogger{T}"/> and being lost. A plain
/// assertion that "a warning happened" would pass for any warning at all — capturing the rendered text
/// lets a test check the message actually names the specific thing it claims to.
/// </summary>
/// <typeparam name="T">The type the logger is created for — matches the constructor parameter type an <see cref="ILogger{T}"/>-typed dependency expects.</typeparam>
public sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>Every message rendered through this logger so far, in the order logged.</summary>
    public List<string> Messages { get; } = [];

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
}
