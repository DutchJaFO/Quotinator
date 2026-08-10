using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

/// <summary>Join strategy for a batch of Characters' active linked Sources — see <see cref="Sql.CharacterSources.SelectSourceReferencesForCharacters"/>.</summary>
public sealed class CharacterSourceReferencesBatchStrategy : IJoinStrategy<LinkRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.CharacterSources.SelectSourceReferencesForCharacters;
}
