using System.Data;
using Dapper;
using Quotinator.Data.Models;

#pragma warning disable CS8765 // Nullability mismatch on override: Dapper's TypeHandler base uses non-nullable object but we handle DBNull defensively.

namespace Quotinator.Data.Helpers;

/// <summary>
/// Dapper TypeHandler for <see cref="SafeValue{T}"/> where T is a nullable enum.
/// Stores the enum name as TEXT. On read, if the stored string no longer matches any
/// enum member (e.g. after a rename), <see cref="SafeValue{T}.Parsed"/> is null and
/// <see cref="SafeValue{T}.Raw"/> preserves the original value for diagnosis.
/// </summary>
public sealed class SafeEnumHandler<TEnum> : SqlMapper.TypeHandler<SafeValue<TEnum?>>
    where TEnum : struct, Enum
{
    /// <inheritdoc/>
    public override SafeValue<TEnum?> Parse(object value)
    {
        if (value is DBNull || value is null)
            return SafeValue<TEnum?>.Empty;

        var raw = value.ToString() ?? string.Empty;
        TEnum? parsed = Enum.TryParse<TEnum>(raw, ignoreCase: true, out var result)
            ? result
            : null;
        return new(raw, parsed);
    }

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, SafeValue<TEnum?> value)
    {
        if (string.IsNullOrEmpty(value.Raw))
            parameter.Value = DBNull.Value;
        else
            parameter.Value = value.Raw;
    }
}
