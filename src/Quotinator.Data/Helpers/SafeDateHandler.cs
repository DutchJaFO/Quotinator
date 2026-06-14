using System.Data;
using System.Globalization;
using Dapper;
using Quotinator.Data.Models;

#pragma warning disable CS8765 // Nullability mismatch on override: Dapper's TypeHandler base uses non-nullable object but we handle DBNull defensively.

namespace Quotinator.Data.Helpers;

/// <summary>
/// Dapper TypeHandler for <see cref="SafeValue{T}"/> where T is <see cref="Nullable{T}">DateTime?</see>.
/// Supports both full UTC audit timestamps ("yyyy-MM-dd HH:mm:ss") and imprecise publication /
/// biographical dates ("yyyy-MM-dd", "yyyy-MM", "yyyy").
/// When the stored string cannot be parsed, <see cref="SafeValue{T}.Parsed"/> is null and
/// <see cref="SafeValue{T}.Raw"/> preserves the original value for diagnosis.
/// Parsed values carry <see cref="DateTimeKind.Utc"/> so callers can convert to local time.
/// </summary>
public sealed class SafeDateHandler : SqlMapper.TypeHandler<SafeValue<DateTime?>>
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd",
        "yyyy-MM",
        "yyyy"
    ];

    /// <inheritdoc/>
    public override SafeValue<DateTime?> Parse(object value)
    {
        if (value is DBNull || value is null)
            return SafeDateValue.Empty;

        var raw = value.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return SafeDateValue.Empty;

        DateTime? parsed = DateTime.TryParseExact(
            raw, Formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var dt)
            ? dt
            : null;

        return new(raw, parsed);
    }

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, SafeValue<DateTime?> value)
    {
        if (string.IsNullOrEmpty(value.Raw))
            parameter.Value = DBNull.Value;
        else
            parameter.Value = value.Raw;
    }
}
