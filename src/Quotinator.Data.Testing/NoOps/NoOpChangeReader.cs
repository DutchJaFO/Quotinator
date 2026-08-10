using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="IChangeReader"/> for use in unit tests that do not exercise change-log read behaviour — always returns an empty result.</summary>
public sealed class NoOpChangeReader : IChangeReader
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpChangeReader Instance = new();

    /// <inheritdoc/>
    public Task<IReadOnlyList<ChangeEntity>> GetHistoryAsync(string entityType, string entityId)
        => Task.FromResult<IReadOnlyList<ChangeEntity>>([]);

    /// <inheritdoc/>
    public Task<IReadOnlyList<ChangeEntity>> GetAllInRangeAsync(DateTime? startDate, DateTime? endDate)
        => Task.FromResult<IReadOnlyList<ChangeEntity>>([]);

    /// <inheritdoc/>
    public Task<int> CountInRangeAsync(DateTime? startDate, DateTime? endDate)
        => Task.FromResult(0);

    /// <inheritdoc/>
    public Task<(DateTime? Earliest, DateTime? Latest)> GetDateRangeAsync()
        => Task.FromResult<(DateTime?, DateTime?)>((null, null));
}
