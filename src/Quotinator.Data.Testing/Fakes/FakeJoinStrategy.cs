using Quotinator.Data.Queries;

namespace Quotinator.Data.Testing.Fakes;

/// <summary>
/// Configurable <see cref="IJoinStrategy{TResult}"/> for unit tests.
/// Constructed with a SQL string; <see cref="BuildSql"/> returns it unchanged.
/// </summary>
/// <typeparam name="TResult">The read model type this strategy targets.</typeparam>
public sealed class FakeJoinStrategy<TResult>(string sql) : IJoinStrategy<TResult>
{
    /// <inheritdoc/>
    public string BuildSql() => sql;
}
