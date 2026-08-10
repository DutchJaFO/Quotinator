using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

/// <summary>Join strategy for a batch of Series' active Universe references — see <see cref="Sql.Series.SelectUniverseReferencesForSeries"/>.</summary>
public sealed class SeriesUniverseReferencesBatchStrategy : IJoinStrategy<SeriesUniverseReferenceRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.Series.SelectUniverseReferencesForSeries;
}
