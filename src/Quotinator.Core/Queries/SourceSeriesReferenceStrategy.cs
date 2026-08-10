using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

/// <summary>Join strategy for a single Source's active Series reference — see <see cref="Sql.Sources.SelectSeriesReferenceForSource"/>.</summary>
public sealed class SourceSeriesReferenceStrategy : IJoinStrategy<SeriesReferenceRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.Sources.SelectSeriesReferenceForSource;
}
