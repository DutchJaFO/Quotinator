using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

/// <summary>Join strategy for a single Source's active Season reference — see <see cref="Sql.Sources.SelectSeasonReferenceForSource"/>.</summary>
public sealed class SourceSeasonReferenceStrategy : IJoinStrategy<SourceSeasonReferenceRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.Sources.SelectSeasonReferenceForSource;
}
