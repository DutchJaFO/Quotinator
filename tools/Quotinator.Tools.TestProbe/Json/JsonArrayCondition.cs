namespace Quotinator.Tools.TestProbe.Json;

/// <summary>
/// One <c>--where</c> option: keep only the elements of the array at <paramref name="ArrayPath"/> whose
/// <paramref name="Field"/> holds <paramref name="Value"/>, or, when <paramref name="Value"/> is null, whose
/// <paramref name="Field"/> exists at all.
/// </summary>
/// <param name="ArrayPath">The dotted path of the array to narrow.</param>
/// <param name="Field">The dotted path, inside each element, of the field compared.</param>
/// <param name="Value">The value the field must hold, compared exactly as rendered; null to require only that it exists.</param>
internal sealed record JsonArrayCondition(string ArrayPath, string Field, string? Value);
