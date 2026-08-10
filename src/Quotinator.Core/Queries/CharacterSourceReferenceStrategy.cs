using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

/// <summary>Join strategy for a single Character's active linked Sources — see <see cref="Sql.CharacterSources.SelectSourceReferencesForCharacter"/>.</summary>
public sealed class CharacterSourceReferenceStrategy : IJoinStrategy<SourceRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.CharacterSources.SelectSourceReferencesForCharacter;
}
