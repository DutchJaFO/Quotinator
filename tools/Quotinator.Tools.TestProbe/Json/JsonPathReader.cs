using System.Text.Json;

namespace Quotinator.Tools.TestProbe.Json;

/// <summary>Resolves a dotted property path inside a JSON document.</summary>
internal static class JsonPathReader
{
    /// <summary>
    /// Resolves <paramref name="path"/>, property names joined by dots, from <paramref name="root"/>.
    /// Property names are matched exactly as written.
    /// </summary>
    /// <param name="root">The document's root element.</param>
    /// <param name="path">The dotted path.</param>
    /// <param name="value">The element found, when the result is true.</param>
    internal static bool TryRead(JsonElement root, string path, out JsonElement value)
    {
        value = root;

        foreach (string name in path.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out JsonElement next))
            {
                value = default;
                return false;
            }

            value = next;
        }

        return true;
    }

    /// <summary>The element's value as plain text: a string unquoted, anything else as its JSON text.</summary>
    /// <param name="element">The element to render.</param>
    internal static string Render(JsonElement element)
        => element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();
}
