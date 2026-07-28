using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Singleton implementation of <see cref="IInitiatorContext"/> (and, via it, <see cref="ICallerContext"/>)
/// backed by <see cref="AsyncLocal{T}"/>. Each async execution context (an HTTP request, or a startup
/// seeding run) maintains its own values without lifetime conflicts between singleton repositories and
/// scoped request state — same reasoning as <see cref="CallerContext"/>.
/// </summary>
/// <remarks>
/// Registered in place of <see cref="CallerContext"/> for both the <see cref="ICallerContext"/> and
/// <see cref="IInitiatorContext"/> DI registrations, so existing <see cref="SqliteRepository{T}"/>
/// consumers reading <see cref="Agent"/> are unaffected by this type's introduction.
/// </remarks>
public sealed class InitiatorContext : IInitiatorContext
{
    private readonly AsyncLocal<string?> _agent = new();
    private readonly AsyncLocal<InitiatorType?> _initiatedByType = new();
    private readonly AsyncLocal<string?> _initiatedById = new();

    /// <inheritdoc/>
    public string? Agent
    {
        get => _agent.Value;
        set => _agent.Value = value;
    }

    /// <inheritdoc/>
    public InitiatorType? InitiatedByType
    {
        get => _initiatedByType.Value;
        set => _initiatedByType.Value = value;
    }

    /// <inheritdoc/>
    public string? InitiatedById
    {
        get => _initiatedById.Value;
        set => _initiatedById.Value = value;
    }
}
