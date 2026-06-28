namespace Quotinator.Data.Repositories;

/// <summary>
/// Singleton implementation of <see cref="ICallerContext"/> backed by <see cref="AsyncLocal{T}"/>.
/// Each async execution context (HTTP request) maintains its own <see cref="Agent"/> value.
/// </summary>
/// <remarks>
/// Registered as a singleton so it can be injected into singleton repositories without a
/// lifetime mismatch. <see cref="AsyncLocal{T}"/> provides the per-request isolation that
/// scoped lifetime would otherwise give.
/// </remarks>
public sealed class CallerContext : ICallerContext
{
    private readonly AsyncLocal<string?> _agent = new();

    /// <inheritdoc/>
    public string? Agent
    {
        get => _agent.Value;
        set => _agent.Value = value;
    }
}
