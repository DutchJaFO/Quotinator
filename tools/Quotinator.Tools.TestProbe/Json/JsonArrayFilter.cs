using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Quotinator.Tools.TestProbe.Json;

/// <summary>
/// Narrows an array in a JSON document to the elements matching the <c>--where</c> options, before any
/// expectation is checked, so a document asserts on the rows of one kind in a list that has no filter of
/// its own. The document's shape is whatever the endpoint returned, which is why it is rewritten
/// generically here rather than deserialized into a type.
/// </summary>
internal static class JsonArrayFilter
{
    private const string WhereOption = "--where";

    /// <summary>The <c>--where</c> options in <paramref name="args"/>, each <c>array:field=value</c> or <c>array:field</c>.</summary>
    /// <param name="args">The command's arguments.</param>
    /// <param name="remaining">Every other argument, in order.</param>
    /// <param name="errors">One line per unreadable option; empty when all were read.</param>
    internal static IReadOnlyList<JsonArrayCondition> Parse(IReadOnlyList<string> args, out IReadOnlyList<string> remaining, out IReadOnlyList<string> errors)
    {
        List<JsonArrayCondition> conditions = [];
        List<string> others = [];
        List<string> problems = [];

        for (int index = 0; index < args.Count; index++)
        {
            if (args[index] != WhereOption)
            {
                others.Add(args[index]);
                continue;
            }

            if (index + 1 >= args.Count)
            {
                problems.Add($"{WhereOption} needs array:field=value or array:field");
                break;
            }

            string argument = args[++index];
            int colon = argument.IndexOf(':');

            if (colon <= 0 || colon == argument.Length - 1)
            {
                problems.Add($"{WhereOption} needs array:field=value or array:field, got '{argument}'");
                continue;
            }

            string condition = argument[(colon + 1)..];
            int equals = condition.IndexOf('=');

            if (equals == 0)
            {
                problems.Add($"{WhereOption} needs a field before '=', got '{argument}'");
                continue;
            }

            conditions.Add(equals < 0
                ? new JsonArrayCondition(argument[..colon], condition, null)
                : new JsonArrayCondition(argument[..colon], condition[..equals], condition[(equals + 1)..]));
        }

        remaining = others;
        errors = problems;
        return conditions;
    }

    /// <summary>
    /// The document with each condition's array narrowed to the elements matching it, conditions on the
    /// same array narrowing it in turn, or null with a <paramref name="problem"/> when a condition names no
    /// array.
    /// </summary>
    /// <param name="json">The document.</param>
    /// <param name="conditions">The conditions, all applied.</param>
    /// <param name="problem">Why nothing was returned.</param>
    internal static string? Apply(string json, IReadOnlyList<JsonArrayCondition> conditions, out string? problem)
    {
        foreach (JsonArrayCondition condition in conditions)
        {
            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException exception)
            {
                problem = $"the body is not JSON: {exception.Message}";
                return null;
            }

            using JsonDocument parsed = document;

            if (!JsonPathReader.TryRead(parsed.RootElement, condition.ArrayPath, out JsonElement target) || target.ValueKind != JsonValueKind.Array)
            {
                problem = $"{WhereOption}: no array at '{condition.ArrayPath}'";
                return null;
            }

            using MemoryStream stream = new();

            using (Utf8JsonWriter writer = new(stream))
            {
                Write(writer, parsed.RootElement, string.Empty, condition);
            }

            json = Encoding.UTF8.GetString(stream.ToArray());
        }

        problem = null;
        return json;
    }

    /// <summary>One line per condition, naming it, so a run records which elements were asserted on.</summary>
    /// <param name="conditions">The conditions applied.</param>
    internal static IEnumerable<string> Describe(IReadOnlyList<JsonArrayCondition> conditions)
        => conditions.Select(condition => condition.Value is null
            ? $"where {condition.ArrayPath}: {condition.Field} exists"
            : $"where {condition.ArrayPath}: {condition.Field} = {condition.Value}");

    private static void Write(Utf8JsonWriter writer, JsonElement element, string path, JsonArrayCondition condition)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value, Child(path, property.Name), condition);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                int index = 0;

                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (path != condition.ArrayPath || Matches(item, condition))
                    {
                        Write(writer, item, Child(path, index.ToString(CultureInfo.InvariantCulture)), condition);
                    }

                    index++;
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool Matches(JsonElement item, JsonArrayCondition condition)
        => JsonPathReader.TryRead(item, condition.Field, out JsonElement field)
            && (condition.Value is null || JsonPathReader.Render(field) == condition.Value);

    private static string Child(string path, string segment) => path.Length == 0 ? segment : $"{path}.{segment}";
}
