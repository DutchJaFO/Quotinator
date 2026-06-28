using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="ICallerContext"/> for use in unit tests — agent is always <see langword="null"/>.</summary>
public sealed class NoOpCallerContext : ICallerContext
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpCallerContext Instance = new();

    /// <inheritdoc/>
    public string? Agent { get; set; }
}
