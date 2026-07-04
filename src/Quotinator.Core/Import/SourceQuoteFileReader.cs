using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quotinator.Core.Import;

/// <summary>Parses a Quotinator source file's raw JSON text into <see cref="SourceQuote"/> entries.</summary>
public static class SourceQuoteFileReader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Attempts to parse <paramref name="json"/> as either a bare <see cref="SourceQuote"/> array or a
    /// <c>{ "quotes": [...] }</c> wrapper. Returns <c>false</c> (with <paramref name="quotes"/> <c>null</c>)
    /// on invalid JSON, an unrecognised top-level shape, or any entry missing a required field — never throws.
    /// </summary>
    /// <param name="json">Raw file contents to parse.</param>
    /// <param name="quotes">The parsed quotes on success; <c>null</c> on failure.</param>
    public static bool TryParse(string json, out List<SourceQuote>? quotes)
    {
        try
        {
            // JsonNode.Parse here only sniffs whether the root is a bare array or a {"quotes":[...]}
            // wrapper — the one shape-sniffing exception CLAUDE.md's JSON parsing policy allows. Actual
            // field extraction always goes through JsonSerializer.Deserialize<List<SourceQuote>>.
            var root = JsonNode.Parse(json);

            if (root is JsonArray)
            {
                quotes = JsonSerializer.Deserialize<List<SourceQuote>>(json, Options) ?? [];
                return true;
            }

            var quotesNode = root?["quotes"];
            if (quotesNode is null)
            {
                quotes = [];
                return true;
            }

            quotes = quotesNode.Deserialize<List<SourceQuote>>(Options) ?? [];
            return true;
        }
        catch (JsonException)
        {
            quotes = null;
            return false;
        }
    }
}
