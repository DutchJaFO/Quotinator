using System.Data;
using System.Text.Json;
using Dapper;

namespace Quotinator.Data.Helpers;

/// <summary>
/// Dapper TypeHandler for <see cref="IReadOnlyList{T}">IReadOnlyList&lt;string&gt;</see> stored as a
/// JSON array in a TEXT column (e.g. <c>NoValueKnown</c>). A missing or empty column value round-trips
/// to an empty list rather than <c>null</c>, matching the column's <c>NOT NULL DEFAULT '[]'</c> schema.
/// </summary>
public sealed class JsonStringListHandler : SqlMapper.TypeHandler<IReadOnlyList<string>>
{
    /// <inheritdoc/>
    public override IReadOnlyList<string> Parse(object value)
    {
        if (value is DBNull || value is null)
            return [];

        var raw = value.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return JsonSerializer.Deserialize<List<string>>(raw) ?? [];
    }

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, IReadOnlyList<string>? value)
        => parameter.Value = JsonSerializer.Serialize(value ?? []);
}
