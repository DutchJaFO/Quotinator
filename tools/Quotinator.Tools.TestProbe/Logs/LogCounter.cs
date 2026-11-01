using System.Globalization;
using System.Text.RegularExpressions;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Logs;

/// <summary>Counts a pattern in log text and checks the count.</summary>
internal static partial class LogCounter
{
    /// <summary>
    /// The newest timestamp in a log read with <c>docker logs --timestamps</c>, every line of which starts
    /// with the daemon's RFC 3339 timestamp; <see cref="DateTimeOffset.MinValue"/> for an empty log.
    /// </summary>
    internal static DateTimeOffset LastTimestamp(string timestampedLog)
    {
        DateTimeOffset last = DateTimeOffset.MinValue;

        foreach (string line in timestampedLog.Split('\n'))
        {
            if (TryReadStamp(line, out DateTimeOffset stamp) && stamp > last)
            {
                last = stamp;
            }
        }

        return last;
    }

    /// <summary>The lines of a timestamped log stamped strictly after <paramref name="mark"/>.</summary>
    internal static string LinesAfter(string timestampedLog, DateTimeOffset mark)
        => string.Join('\n', timestampedLog.Split('\n').Where(line => TryReadStamp(line, out DateTimeOffset stamp) && stamp > mark));

    /// <summary>
    /// Reads the RFC 3339 timestamp <c>docker logs --timestamps</c> puts at the start of a line. Docker
    /// writes nine fractional digits and .NET parses at most seven, so the fraction is cut to seven first.
    /// </summary>
    private static bool TryReadStamp(string line, out DateTimeOffset stamp)
    {
        int space = line.IndexOf(' ');
        string token = space < 0 ? line : line[..space];
        Match parts = StampPattern().Match(token);

        if (!parts.Success)
        {
            stamp = default;
            return false;
        }

        string fraction = parts.Groups["fraction"].Value;
        string trimmed = fraction.Length > 8 ? fraction[..8] : fraction;

        return DateTimeOffset.TryParse(parts.Groups["seconds"].Value + trimmed + "Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out stamp);
    }

    [GeneratedRegex(@"^(?<seconds>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?<fraction>\.\d+)?Z$")]
    private static partial Regex StampPattern();

    /// <summary>Occurrences of the regular expression <paramref name="pattern"/> in <paramref name="text"/>, not matching lines.</summary>
    internal static int Count(string text, string pattern) => Regex.Count(text, pattern);

    /// <summary>
    /// Holds when <paramref name="count"/> is at least <paramref name="atLeast"/> or exactly
    /// <paramref name="exactly"/>, whichever is given; observes the count. Neither, or both, is a failure.
    /// </summary>
    internal static ExpectationResult Check(int count, int? atLeast, int? exactly)
    {
        string observed = $"occurrences: {count}";

        if (atLeast.HasValue == exactly.HasValue)
        {
            return ExpectationResult.Failed("give exactly one of --at-least and --exactly", observed);
        }

        if (atLeast.HasValue)
        {
            return count >= atLeast.Value
                ? ExpectationResult.Passed(observed)
                : ExpectationResult.Failed($"expected at least {atLeast.Value} occurrences, found {count}", observed);
        }

        return count == exactly!.Value
            ? ExpectationResult.Passed(observed)
            : ExpectationResult.Failed($"expected exactly {exactly.Value} occurrences, found {count}", observed);
    }
}
