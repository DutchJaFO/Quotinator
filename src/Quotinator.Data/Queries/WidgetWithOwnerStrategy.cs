using Quotinator.Data.Models;

namespace Quotinator.Data.Queries;

/// <summary>Join strategy for Widget with its Owner — canonical example for the <c>IJoinStrategy&lt;TResult&gt;</c> pattern.</summary>
public sealed class WidgetWithOwnerStrategy : IJoinStrategy<WidgetWithOwner>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.Queries.WidgetWithOwner();
}
