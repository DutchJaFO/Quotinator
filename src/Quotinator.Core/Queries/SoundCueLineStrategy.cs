using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

/// <summary>Join strategy for a single translation-resolved SoundCue — see <see cref="Sql.SoundCues.SelectByIdWithTranslation"/>.</summary>
public sealed class SoundCueLineStrategy : IJoinStrategy<SoundCueLineRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.SoundCues.SelectByIdWithTranslation;
}
