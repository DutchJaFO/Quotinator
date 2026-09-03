using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

/// <summary>Join strategy for a single Season's active Series reference — see <see cref="Sql.Season.SelectSeriesReferenceForSeason"/>.</summary>
public sealed class SeasonSeriesReferenceStrategy : IJoinStrategy<SeasonSeriesReferenceRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.Season.SelectSeriesReferenceForSeason;
}
