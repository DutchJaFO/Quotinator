using System.Text.RegularExpressions;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Logs;

/// <summary>Counts a pattern in log text and checks the count.</summary>
internal static class LogCounter
{
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
