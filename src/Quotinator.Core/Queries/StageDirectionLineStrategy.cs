using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

/// <summary>Join strategy for a single translation-resolved StageDirection — see <see cref="Sql.StageDirections.SelectByIdWithTranslation"/>.</summary>
public sealed class StageDirectionLineStrategy : IJoinStrategy<StageDirectionLineRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.StageDirections.SelectByIdWithTranslation;
}
