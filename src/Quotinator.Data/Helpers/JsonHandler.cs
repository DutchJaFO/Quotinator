using System.Data;
using System.Text.Json;
using Dapper;

namespace Quotinator.Data.Helpers;

/// <summary>
/// Generic Dapper TypeHandler that stores <typeparamref name="T"/> as JSON text in a TEXT column,
/// via <see cref="JsonSerializer"/>. Works for any JSON-serialisable shape — a string list
/// (e.g. <c>NoValueKnown</c>), a string dictionary (e.g. a future typed read of
/// <c>System_ImportConflicts.MergedFields</c>), or any other DTO — not just one hardcoded shape.
/// </summary>
/// <remarks>
/// One instance must be registered per concrete <typeparamref name="T"/> via
/// <see cref="DatabaseConfiguration.RegisterJsonHandler{T}"/> — Dapper's <c>SqlMapper.AddTypeHandler</c>
/// is keyed by the exact closed generic type, so registering <c>JsonHandler&lt;IReadOnlyList&lt;string&gt;&gt;</c>
/// does not also cover <c>JsonHandler&lt;IReadOnlyDictionary&lt;string, string&gt;&gt;</c>.
/// </remarks>
public sealed class JsonHandler<T> : SqlMapper.TypeHandler<T>
{
    /// <inheritdoc/>
    public override T? Parse(object value)
    {
        if (value is DBNull || value is null)
            return default;

        var raw = value.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return default;

        return JsonSerializer.Deserialize<T>(raw);
    }

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, T? value)
        => parameter.Value = value is null ? DBNull.Value : JsonSerializer.Serialize(value);
}
