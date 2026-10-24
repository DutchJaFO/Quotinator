using Quotinator.Tools.TestProbe.Enums;

namespace Quotinator.Tools.TestProbe.Json;

/// <summary>One assertion about one field of a JSON document.</summary>
/// <param name="Kind">What is asserted.</param>
/// <param name="Path">Property names joined by dots.</param>
/// <param name="Value">The expected text for <see cref="JsonExpectationKind.EqualTo"/> and <see cref="JsonExpectationKind.Contains"/>; null otherwise.</param>
internal sealed record JsonExpectation(JsonExpectationKind Kind, string Path, string? Value);
