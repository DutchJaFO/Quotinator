using System.Globalization;
using System.Text.Json;

namespace Quotinator.Tools.TestProbe.Json;

/// <summary>Resolves a dotted property path inside a JSON document.</summary>
internal static class JsonPathReader
{
    /// <summary>
    /// Resolves <paramref name="path"/>, segments joined by dots, from <paramref name="root"/>. A segment
    /// is a property name, matched exactly as written, or a zero-based index when the element is an array.
    /// A path continues into a string that holds a JSON object or array, such as a notification's
    /// <c>metadata</c> (#503); a string ending the path is returned as the string.
    /// </summary>
    /// <param name="root">The document's root element.</param>
    /// <param name="path">The dotted path.</param>
    /// <param name="value">The element found, when the result is true.</param>
    internal static bool TryRead(JsonElement root, string path, out JsonElement value)
    {
        value = root;

        foreach (string segment in path.Split('.'))
        {
            if (!TryReadSegment(value, segment, out JsonElement next))
            {
                value = default;
                return false;
            }

            value = next;
        }

        return true;
    }

    private static bool TryReadSegment(JsonElement element, string segment, out JsonElement next)
    {
        next = default;

        if (element.ValueKind == JsonValueKind.String && !TryParseEmbedded(element, out element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            return element.TryGetProperty(segment, out next);
        }

        if (element.ValueKind != JsonValueKind.Array
            || !int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            || index >= element.GetArrayLength())
        {
            return false;
        }

        next = element[index];
        return true;
    }

    private static bool TryParseEmbedded(JsonElement text, out JsonElement parsed)
    {
        parsed = default;
        string content = (text.GetString() ?? string.Empty).TrimStart();

        if (content.Length == 0 || (content[0] != '{' && content[0] != '['))
        {
            return false;
        }

        try
        {
            using JsonDocument embedded = JsonDocument.Parse(content);
            parsed = embedded.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The element's value as plain text: a string unquoted, anything else as its JSON text.</summary>
    /// <param name="element">The element to render.</param>
    internal static string Render(JsonElement element)
        => element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();
}
