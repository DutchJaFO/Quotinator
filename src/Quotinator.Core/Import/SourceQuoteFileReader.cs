using System.Text.Json;
using System.Text.Json.Nodes;
using Quotinator.Data.Import;

namespace Quotinator.Core.Import;

/// <summary>Parses a Quotinator source file's raw JSON text into <see cref="SourceQuoteDto"/> entries.</summary>
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
    /// Attempts to parse <paramref name="json"/> as either a bare <see cref="SourceQuoteDto"/> array or a
    /// <c>{ "quotes": [...] }</c> wrapper. Returns <c>false</c> (with <paramref name="quotes"/> <c>null</c>)
    /// on invalid JSON, an unrecognised top-level shape, or any entry missing a required field — never throws.
    /// </summary>
    /// <param name="json">Raw file contents to parse.</param>
    /// <param name="quotes">The parsed quotes on success; <c>null</c> on failure.</param>
    public static bool TryParse(string json, out List<SourceQuoteDto>? quotes)
    {
        try
        {
            // JsonNode.Parse here only sniffs whether the root is a bare array or a {"quotes":[...]}
            // wrapper — the one shape-sniffing exception CLAUDE.md's JSON parsing policy allows. Actual
            // field extraction always goes through JsonSerializer.Deserialize<List<SourceQuoteDto>>.
            var root = JsonNode.Parse(json);

            if (root is JsonArray)
            {
                quotes = JsonSerializer.Deserialize<List<SourceQuoteDto>>(json, Options) ?? [];
                return true;
            }

            var quotesNode = root?["quotes"];
            if (quotesNode is null)
            {
                quotes = [];
                return true;
            }

            quotes = quotesNode.Deserialize<List<SourceQuoteDto>>(Options) ?? [];
            return true;
        }
        catch (JsonException)
        {
            quotes = null;
            return false;
        }
    }

    /// <summary>
    /// Attempts to parse <paramref name="json"/> as either a bare <see cref="SourceQuoteDto"/> array or the
    /// full extended object format (<c>{ "quotes": [...], "sources": [...], "people": [...],
    /// "stageDirections": [...], "soundCues": [...], "conversations": [...], "series": [...],
    /// "universe": [...] }</c>). A bare array yields empty lists for every extended section — same
    /// backward-compatibility rule as <see cref="TryParse"/>. Returns <c>false</c> on invalid JSON or
    /// any entry missing a required field — never throws.
    /// </summary>
    /// <param name="json">Raw file contents to parse.</param>
    /// <param name="result">The parsed file on success; <c>null</c> on failure.</param>
    public static bool TryParseExtended(string json, out ParsedSourceFileDto? result)
    {
        try
        {
            // Same single shape-sniffing JsonNode.Parse call as TryParse — see its own remarks for why
            // this is the one permitted exception to the JSON parsing policy. Every section below is
            // still extracted via JsonSerializer.Deserialize<T>, never manual node walking.
            var root = JsonNode.Parse(json);

            if (root is JsonArray)
            {
                var quotes = JsonSerializer.Deserialize<List<SourceQuoteDto>>(json, Options) ?? [];
                result = new ParsedSourceFileDto { Quotes = quotes };
                return true;
            }

            var quotesNode = root?["quotes"];
            result = new ParsedSourceFileDto
            {
                Quotes          = quotesNode is null ? [] : quotesNode.Deserialize<List<SourceQuoteDto>>(Options) ?? [],
                Sources         = root?["sources"]?.Deserialize<List<SourceEntryDto>>(Options) ?? [],
                People          = root?["people"]?.Deserialize<List<PersonEntryDto>>(Options) ?? [],
                Characters      = root?["characters"]?.Deserialize<List<CharacterEntryDto>>(Options) ?? [],
                StageDirections = root?["stageDirections"]?.Deserialize<List<SourceStageDirectionDto>>(Options) ?? [],
                SoundCues       = root?["soundCues"]?.Deserialize<List<SourceSoundCueDto>>(Options) ?? [],
                Conversations   = root?["conversations"]?.Deserialize<List<SourceConversationDto>>(Options) ?? [],
                Series          = root?["series"]?.Deserialize<List<SeriesEntryDto>>(Options) ?? [],
                Universe        = root?["universe"]?.Deserialize<List<UniverseEntryDto>>(Options) ?? [],
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
