namespace Quotinator.Tools.TestProbe.Results;

/// <summary>
/// The outcome of a command: what it observed, and every expectation that did not hold. Returned rather
/// than thrown, since a failed expectation is a condition the command has checked (ADR 022).
/// </summary>
/// <param name="Observations">The values the command read, printed so a run records what was seen.</param>
/// <param name="Failures">One line per expectation that did not hold; empty when every one held.</param>
internal sealed record ExpectationResult(IReadOnlyList<string> Observations, IReadOnlyList<string> Failures)
{
    /// <summary>True when no expectation failed.</summary>
    public bool Holds => Failures.Count == 0;

    /// <summary>A result that observed <paramref name="observation"/> and failed nothing.</summary>
    public static ExpectationResult Passed(params string[] observation) => new(observation, []);

    /// <summary>A result whose one expectation failed with <paramref name="failure"/>.</summary>
    public static ExpectationResult Failed(string failure, params string[] observation) => new(observation, [failure]);
}
