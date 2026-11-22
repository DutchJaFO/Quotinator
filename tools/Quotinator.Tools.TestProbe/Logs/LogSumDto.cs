using System.Text.Json.Serialization;

namespace Quotinator.Tools.TestProbe.Logs;

/// <summary>
/// What <c>log --sum-out</c> writes: how many times the pattern matched, and the sum of the number its first
/// group captured each time, so a document compares a count against what the log says happened without
/// copying either into its text.
/// </summary>
/// <param name="Matches">How many times the pattern matched.</param>
/// <param name="Sum">The sum of the first group's number across every match.</param>
internal sealed record LogSumDto(
    [property: JsonPropertyName("matches")] int Matches,
    [property: JsonPropertyName("sum")] int Sum);
