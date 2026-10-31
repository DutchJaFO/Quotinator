using Quotinator.Tools.TestProbe.Enums;

namespace Quotinator.Tools.TestProbe.Json;

/// <summary>One assertion about one field of a JSON document.</summary>
/// <param name="Kind">What is asserted.</param>
/// <param name="Path">Property names joined by dots.</param>
/// <param name="Value">The expected text, or for a kind comparing with another field that field's path; null when the kind takes neither.</param>
internal sealed record JsonExpectation(JsonExpectationKind Kind, string Path, string? Value);
