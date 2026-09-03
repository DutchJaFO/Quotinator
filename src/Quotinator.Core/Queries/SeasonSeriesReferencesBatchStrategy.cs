using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

/// <summary>Join strategy for a batch of Seasons' active Series references — see <see cref="Sql.Season.SelectSeriesReferencesForSeasons"/>.</summary>
public sealed class SeasonSeriesReferencesBatchStrategy : IJoinStrategy<SeasonSeriesReferencesBatchRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.Season.SelectSeriesReferencesForSeasons;
}
