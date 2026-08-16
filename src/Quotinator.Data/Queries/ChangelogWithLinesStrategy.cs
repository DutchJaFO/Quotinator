namespace Quotinator.Data.Queries;

/// <summary>Join strategy for every <c>Changelog_Entry</c> row with its <c>Changelog_Line</c> children flattened (#309).</summary>
public sealed class ChangelogWithLinesStrategy : IJoinStrategy<ChangelogLineRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.ChangelogContent.SelectAllWithLines();
}
