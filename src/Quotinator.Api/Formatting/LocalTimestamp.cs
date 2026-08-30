namespace Quotinator.Api.Formatting;

/// <summary>
/// Renders a stored UTC timestamp in the host's own time zone, for display in the Blazor UI (#304).
/// <para>
/// Every timestamp this application stores is UTC — <c>SafeDateHandler</c> parses them with
/// <see cref="DateTimeKind.Utc"/> explicitly "so callers can convert to local time". The UI did not
/// convert, so every timestamp on every page was shown an offset away from when it happened: found in
/// #304's T1 pass, where an event logged at 16:17 local appeared as 14:17.
/// </para>
/// <para>
/// Converted server-side, exactly as this application's own log timestamps already are. No browser
/// round-trip and no JS interop: the time zone that matters is the host's, and it is the same one every
/// other timestamp the application prints already uses. A deployment wanting different behaviour sets
/// the container's time zone, which is the ordinary lever for this.
/// </para>
/// <para>
/// One helper rather than the formatting repeated per component: the two call sites that existed both
/// had the same defect, which is what a repeated expression tends to produce.
/// </para>
/// </summary>
public static class LocalTimestamp
{
    /// <summary>The display format shared by every timestamp in the UI — date and minutes, no seconds.</summary>
    public const string Format = "yyyy-MM-dd HH:mm";

    /// <summary>Rendered in place of a timestamp that is absent, rather than a blank cell or a default date.</summary>
    public const string Absent = "—";

    /// <summary>
    /// Formats <paramref name="utc"/> in the host's time zone, or returns <see cref="Absent"/> when
    /// there is no value.
    /// </summary>
    /// <param name="utc">
    /// The stored value, or <see langword="null"/>. A value whose <see cref="DateTimeKind"/> is
    /// <see cref="DateTimeKind.Unspecified"/> is treated as UTC — that is what it is, and SQLite hands
    /// values back unspecified, so assuming local would be correct only on a host already running in UTC.
    /// </param>
    public static string Render(DateTime? utc)
        => utc is DateTime value
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime().ToString(Format)
            : Absent;
}
