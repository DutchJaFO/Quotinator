using System.Globalization;
using System.Text.Json;
using Quotinator.Tools.TestProbe.Enums;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Json;

/// <summary>Checks a JSON document against expectations.</summary>
internal static class JsonExpectationEvaluator
{
    /// <summary>
    /// Evaluates every expectation against <paramref name="json"/>, observing each field's value. Text
    /// that is not JSON fails every expectation, as does an empty list of expectations, since a check that
    /// asserts nothing cannot fail.
    /// </summary>
    /// <param name="json">The document.</param>
    /// <param name="expectations">What must hold.</param>
    internal static ExpectationResult Evaluate(string json, IReadOnlyList<JsonExpectation> expectations)
    {
        if (expectations.Count == 0)
        {
            return ExpectationResult.Failed("no expectation given; a check that asserts nothing cannot fail");
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return ExpectationResult.Failed($"the body is not JSON: {exception.Message}", $"body: {json}");
        }

        using (document)
        {
            List<string> observations = [];
            List<string> failures = [];

            foreach (JsonExpectation expectation in expectations)
            {
                bool found = JsonPathReader.TryRead(document.RootElement, expectation.Path, out JsonElement value);
                observations.Add(found ? Observe(expectation, value) : $"{expectation.Path} is absent");

                string? failure = Check(document.RootElement, expectation, found, value);

                if (failure is not null)
                {
                    failures.Add(failure);
                }
            }

            return new ExpectationResult(observations, failures);
        }
    }

    private static string? Check(JsonElement root, JsonExpectation expectation, bool found, JsonElement value)
    {
        if (expectation.Kind == JsonExpectationKind.Absent)
        {
            return found ? $"{expectation.Path}: expected {Describe(expectation)}, got: {Observe(expectation, value)}" : null;
        }

        if (!found)
        {
            return $"{expectation.Path}: expected {Describe(expectation)}, but the field is absent";
        }

        bool holds = expectation.Kind switch
        {
            JsonExpectationKind.EqualTo => JsonPathReader.Render(value) == expectation.Value,
            JsonExpectationKind.Contains => Contains(value, expectation.Value ?? string.Empty),
            JsonExpectationKind.Present => true,
            JsonExpectationKind.NotEmpty => IsNotEmpty(value),
            JsonExpectationKind.GreaterThan => IsGreaterThan(value, expectation.Value),
            JsonExpectationKind.EqualToField => ReadOther(root, expectation) == JsonPathReader.Render(value),
            JsonExpectationKind.LengthEqualToField => LengthText(value) == ReadOther(root, expectation),
            JsonExpectationKind.LengthEquals => LengthText(value) == expectation.Value,
            _ => false,
        };

        return holds ? null : $"{expectation.Path}: expected {Describe(expectation)}, got: {Observe(expectation, value)}";
    }

    private static string Observe(JsonExpectation expectation, JsonElement value)
        => expectation.Kind is JsonExpectationKind.LengthEqualToField or JsonExpectationKind.LengthEquals && LengthText(value) is string length
            ? $"{expectation.Path} has {length} elements"
            : $"{expectation.Path} = {JsonPathReader.Render(value)}";

    private static string? ReadOther(JsonElement root, JsonExpectation expectation)
        => JsonPathReader.TryRead(root, expectation.Value ?? string.Empty, out JsonElement other) ? JsonPathReader.Render(other) : null;

    private static string? LengthText(JsonElement value)
        => value.ValueKind == JsonValueKind.Array ? value.GetArrayLength().ToString(CultureInfo.InvariantCulture) : null;

    private static bool Contains(JsonElement value, string text) => value.ValueKind switch
    {
        JsonValueKind.String => (value.GetString() ?? string.Empty).Contains(text, StringComparison.Ordinal),
        JsonValueKind.Array => value.EnumerateArray().Any(element => JsonPathReader.Render(element).Contains(text, StringComparison.Ordinal)),
        _ => false,
    };

    private static bool IsNotEmpty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => false,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() > 0,
        _ => true,
    };

    private static bool IsGreaterThan(JsonElement value, string? bound)
        => value.ValueKind == JsonValueKind.Number
            && decimal.TryParse(bound, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal limit)
            && value.GetDecimal() > limit;

    private static string Describe(JsonExpectation expectation) => expectation.Kind switch
    {
        JsonExpectationKind.EqualTo => $"'{expectation.Value}'",
        JsonExpectationKind.GreaterThan => $"a number greater than {expectation.Value}",
        JsonExpectationKind.Contains => $"to contain '{expectation.Value}'",
        JsonExpectationKind.Present => "to be present",
        JsonExpectationKind.Absent => "the field to be absent",
        JsonExpectationKind.EqualToField => $"the value of {expectation.Value}",
        JsonExpectationKind.LengthEqualToField => $"an array as long as the value of {expectation.Value}",
        JsonExpectationKind.LengthEquals => $"an array of {expectation.Value} elements",
        _ => "to be not empty",
    };
}
