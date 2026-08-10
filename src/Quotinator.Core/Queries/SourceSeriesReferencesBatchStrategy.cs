using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

/// <summary>Join strategy for a batch of Sources' active Series references — see <see cref="Sql.Sources.SelectSeriesReferencesForSources"/>.</summary>
public sealed class SourceSeriesReferencesBatchStrategy : IJoinStrategy<SourceSeriesReferenceRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.Sources.SelectSeriesReferencesForSources;
}
