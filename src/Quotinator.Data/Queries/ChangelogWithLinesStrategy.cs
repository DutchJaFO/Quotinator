namespace Quotinator.Data.Queries;

/// <summary>Join strategy for every <c>Changelog</c> row with its <c>ChangelogLine</c> children flattened (#309).</summary>
public sealed class ChangelogWithLinesStrategy : IJoinStrategy<ChangelogLineRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.ChangelogContent.SelectAllWithLines();
}
