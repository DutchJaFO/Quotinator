namespace Quotinator.Tools.DbInspector;

/// <summary>
/// Writes a query's rows as a JSON array of objects keyed by column name, so a test document can assert on
/// a value with TestProbe's <c>json</c> command rather than reading the printed table.
/// </summary>
internal static class JsonRowWriter
{
    /// <summary>The rows as a JSON array, one object per row, each column a property.</summary>
    /// <param name="rows">The rows Dapper returned, as column-to-value dictionaries.</param>
    internal static string ToJson(IEnumerable<IDictionary<string, object>> rows)
        => System.Text.Json.JsonSerializer.Serialize(rows.ToList());
}
