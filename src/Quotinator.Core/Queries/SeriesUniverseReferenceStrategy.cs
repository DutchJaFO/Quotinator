using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

/// <summary>Join strategy for a single Series' active Universe reference — see <see cref="Sql.Series.SelectUniverseReferenceForSeries"/>.</summary>
public sealed class SeriesUniverseReferenceStrategy : IJoinStrategy<UniverseReferenceRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.Series.SelectUniverseReferenceForSeries;
}
