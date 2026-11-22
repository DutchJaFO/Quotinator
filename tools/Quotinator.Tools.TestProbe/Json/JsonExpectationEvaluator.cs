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
                bool found = JsonPathReader.TryRead(document.RootElement, ReadPath(expectation), out JsonElement value);
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
            JsonExpectationKind.DistinctLengthEquals => DistinctText(value, FieldOf(expectation)) == expectation.Value,
            _ => false,
        };

        return holds ? null : $"{expectation.Path}: expected {Describe(expectation)}, got: {Observe(expectation, value)}";
    }

    private static string Observe(JsonExpectation expectation, JsonElement value)
    {
        if (expectation.Kind == JsonExpectationKind.DistinctLengthEquals)
        {
            return DistinctText(value, FieldOf(expectation)) is string distinct
                ? $"{expectation.Path} has {distinct} distinct values"
                : $"{expectation.Path}: not an array whose every element carries the field";
        }

        return expectation.Kind is JsonExpectationKind.LengthEqualToField or JsonExpectationKind.LengthEquals && LengthText(value) is string length
            ? $"{expectation.Path} has {length} elements"
            : $"{expectation.Path} = {JsonPathReader.Render(value)}";
    }

    /// <summary>The path to read: the array part of an <c>array:field</c> path, or the path itself.</summary>
    private static string ReadPath(JsonExpectation expectation)
        => expectation.Kind == JsonExpectationKind.DistinctLengthEquals && expectation.Path.IndexOf(':') is int colon and > 0
            ? expectation.Path[..colon]
            : expectation.Path;

    /// <summary>The field part of an <c>array:field</c> path; empty when the path names none.</summary>
    private static string FieldOf(JsonExpectation expectation)
        => expectation.Path.IndexOf(':') is int colon and > 0 ? expectation.Path[(colon + 1)..] : string.Empty;

    /// <summary>
    /// How many distinct values <paramref name="field"/> takes across the array's elements; null when the
    /// value is not an array, no field is named, or an element does not carry it.
    /// </summary>
    private static string? DistinctText(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Array || field.Length == 0)
        {
            return null;
        }

        HashSet<string> distinct = [];

        foreach (JsonElement element in value.EnumerateArray())
        {
            if (!JsonPathReader.TryRead(element, field, out JsonElement fieldValue))
            {
                return null;
            }

            distinct.Add(JsonPathReader.Render(fieldValue));
        }

        return distinct.Count.ToString(CultureInfo.InvariantCulture);
    }

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
        JsonExpectationKind.DistinctLengthEquals => $"{expectation.Value} distinct values",
        _ => "to be not empty",
    };
}
