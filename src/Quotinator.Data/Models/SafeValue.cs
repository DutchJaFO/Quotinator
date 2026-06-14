using System.Globalization;

namespace Quotinator.Data.Models;

/// <summary>
/// Carries both the raw string from the database and the parsed result.
/// When <see cref="IsValid"/> is false, <see cref="Raw"/> reveals the original value so corruption
/// can be diagnosed and corrected without any data loss.
/// </summary>
public sealed record SafeValue<T>(string Raw, T Parsed)
{
    /// <summary>The original string as stored in the database.</summary>
    public string Raw { get; } = Raw;

    /// <summary>The interpreted value. <c>null</c> when <see cref="Raw"/> could not be converted.</summary>
    public T Parsed { get; } = Parsed;

    /// <summary><c>true</c> when <see cref="Parsed"/> is non-null.</summary>
    public bool IsValid => Parsed is not null;

    /// <summary>A <see cref="SafeValue{T}"/> with an empty <see cref="Raw"/> string and the default <see cref="Parsed"/> value.</summary>
    public static SafeValue<T> Empty => new(string.Empty, default!);
}

/// <summary>Factory for creating <see cref="SafeValue{T}"/> instances for date/time values.</summary>
public static class SafeDateValue
{
    /// <summary>The standard UTC timestamp format used for all audit timestamps.</summary>
    public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>A <see cref="SafeValue{T}"/> wrapping the current UTC time.</summary>
    public static SafeValue<DateTime?> Now
    {
        get
        {
            var dt = DateTime.UtcNow;
            return new(dt.ToString(TimestampFormat, CultureInfo.InvariantCulture), dt);
        }
    }

    /// <summary>Wraps an existing <see cref="DateTime"/> as a <see cref="SafeValue{T}"/>.</summary>
    public static SafeValue<DateTime?> From(DateTime dt)
        => new(dt.ToString(TimestampFormat, CultureInfo.InvariantCulture), dt);

    /// <summary>A <see cref="SafeValue{T}"/> with no date.</summary>
    public static SafeValue<DateTime?> Empty => new(string.Empty, null);
}
