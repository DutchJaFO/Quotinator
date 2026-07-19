using System.Text.Json;
using System.Text.Json.Nodes;
using Quotinator.Data.Import;

namespace Quotinator.Core.Import;

/// <summary>Parses a Quotinator source file's raw JSON text into <see cref="SourceQuote"/> entries.</summary>
public static class SourceQuoteFileReader
{
    // #190: OptionalJsonConverterFactory covers every Optional<T>-typed entry-DTO property (Date,
    // SeriesName, DateOfBirth, etc.) with this one registration — no per-property attribute needed.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new OptionalJsonConverterFactory() },
    };

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

    /// <summary>
    /// Attempts to parse <paramref name="json"/> as either a bare <see cref="SourceQuote"/> array or the
    /// full extended object format (<c>{ "quotes": [...], "sources": [...], "people": [...],
    /// "stageDirections": [...], "soundCues": [...], "conversations": [...], "series": [...],
    /// "universe": [...] }</c>). A bare array yields empty lists for every extended section — same
    /// backward-compatibility rule as <see cref="TryParse"/>. Returns <c>false</c> on invalid JSON or
    /// any entry missing a required field — never throws.
    /// </summary>
    /// <param name="json">Raw file contents to parse.</param>
    /// <param name="result">The parsed file on success; <c>null</c> on failure.</param>
    public static bool TryParseExtended(string json, out ParsedSourceFile? result)
    {
        try
        {
            // Same single shape-sniffing JsonNode.Parse call as TryParse — see its own remarks for why
            // this is the one permitted exception to the JSON parsing policy. Every section below is
            // still extracted via JsonSerializer.Deserialize<T>, never manual node walking.
            var root = JsonNode.Parse(json);

            if (root is JsonArray)
            {
                var quotes = JsonSerializer.Deserialize<List<SourceQuote>>(json, Options) ?? [];
                result = new ParsedSourceFile { Quotes = quotes };
                return true;
            }

            var quotesNode = root?["quotes"];
            result = new ParsedSourceFile
            {
                Quotes          = quotesNode is null ? [] : quotesNode.Deserialize<List<SourceQuote>>(Options) ?? [],
                Sources         = root?["sources"]?.Deserialize<List<SourceEntry>>(Options) ?? [],
                People          = root?["people"]?.Deserialize<List<PersonEntry>>(Options) ?? [],
                StageDirections = root?["stageDirections"]?.Deserialize<List<SourceStageDirection>>(Options) ?? [],
                SoundCues       = root?["soundCues"]?.Deserialize<List<SourceSoundCue>>(Options) ?? [],
                Conversations   = root?["conversations"]?.Deserialize<List<SourceConversation>>(Options) ?? [],
                Series          = root?["series"]?.Deserialize<List<SeriesEntry>>(Options) ?? [],
                Universe        = root?["universe"]?.Deserialize<List<UniverseEntry>>(Options) ?? [],
            };
            return true;
        }
        catch (JsonException)
        {
            result = null;
            return false;
        }
    }
}
