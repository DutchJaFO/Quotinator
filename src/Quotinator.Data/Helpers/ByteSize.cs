using System.Globalization;

namespace Quotinator.Data.Helpers;

/// <summary>Formats a byte count for a person to read (#348).</summary>
public static class ByteSize
{
    private const double Megabyte = 1_048_576d;
    private const double Gigabyte = 1_073_741_824d;

    /// <summary>
    /// <paramref name="bytes"/> in megabytes below one gigabyte, and in gigabytes from there, with the
    /// decimal point written the same in every language, since the text is composed once for all of them.
    /// </summary>
    /// <param name="bytes">The byte count.</param>
    public static string Format(long bytes) =>
        bytes >= Gigabyte
            ? (bytes / Gigabyte).ToString("0.00", CultureInfo.InvariantCulture) + " GB"
            : (bytes / Megabyte).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
}
