using System.Globalization;

namespace Quotinator.Data.Models;

/// <summary>
/// Carries both the raw string from the database and the parsed result.
/// When <see cref="IsValid"/> is false, <see cref="Raw"/> reveals the original value so corruption
/// can be diagnosed and corrected without any data loss.
/// </summary>
public sealed record SafeValue<T>(string Raw, T Parsed)
{
    public bool IsValid => Parsed is not null;

    public static SafeValue<T> Empty => new(string.Empty, default!);
}

/// <summary>Factory for creating <see cref="SafeValue{T}"/> instances for date/time values.</summary>
public static class SafeDateValue
{
    public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    public static SafeValue<DateTime?> Now
    {
        get
        {
            var dt = DateTime.UtcNow;
            return new(dt.ToString(TimestampFormat, CultureInfo.InvariantCulture), dt);
        }
    }

    public static SafeValue<DateTime?> From(DateTime dt)
        => new(dt.ToString(TimestampFormat, CultureInfo.InvariantCulture), dt);

    public static SafeValue<DateTime?> Empty => new(string.Empty, null);
}
