namespace Quotinator.Data.Queries;

/// <summary>Provides the SQL for a join query that returns <typeparamref name="TResult"/> read models.</summary>
/// <typeparam name="TResult">The read model type returned by the query.</typeparam>
public interface IJoinStrategy<TResult>
{
    /// <summary>Returns the full parameterised SELECT … FROM … JOIN … SQL string.</summary>
    string BuildSql();
}
