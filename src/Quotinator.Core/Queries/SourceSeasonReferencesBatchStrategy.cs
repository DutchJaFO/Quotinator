using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

/// <summary>Join strategy for a batch of Sources' active Season references — see <see cref="Sql.Sources.SelectSeasonReferencesForSources"/>.</summary>
public sealed class SourceSeasonReferencesBatchStrategy : IJoinStrategy<SourceSeasonReferencesBatchRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.Sources.SelectSeasonReferencesForSources;
}
