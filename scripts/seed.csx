#!/usr/bin/env dotnet-script
#nullable enable
// Seed script — merges two MIT-licensed movie quote datasets into data/quotes.json.
//
// Sources:
//   vilaboim  — array of "\"Quote text.\" Movie Title" strings
//   NikhilNamal17 — array of { quote, movie, type, year } objects
//
// Run: dotnet script scripts/seed.csx
//      (requires: dotnet tool install -g dotnet-script)

#r "nuget: System.Text.Json, 8.0.0"

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// ── Paths ────────────────────────────────────────────────────────────────────

var repoRoot   = Path.GetFullPath(Path.Combine(Args.Count > 0 ? Args[0] : ".", ".."));
var vilaboim   = Path.Combine(repoRoot, "scripts", "sources", "vilaboim.json");
var nikhil     = Path.Combine(repoRoot, "scripts", "sources", "nikhil.json");
var outputPath = Path.Combine(repoRoot, "data", "quotes.json");

if (!File.Exists(vilaboim) || !File.Exists(nikhil))
{
    Console.Error.WriteLine($"""
        Source files not found. Download them first:

          curl -o scripts/sources/vilaboim.json \
               https://raw.githubusercontent.com/vilaboim/movie-quotes/master/movie-quotes.json

          curl -o scripts/sources/nikhil.json \
               https://raw.githubusercontent.com/NikhilNamal17/popular-movie-quotes/master/data/data.json
        """);
    Environment.Exit(1);
}

// ── Models ───────────────────────────────────────────────────────────────────

record SeedQuote(
    string Id,
    string Quote,
    string Source,
    string? Date,
    string Type,
    string OriginalLanguage = "en");

// ── Helpers ──────────────────────────────────────────────────────────────────

// Deterministic UUID v5-style: SHA-256 of normalised content → first 16 bytes → UUID.
static string StableId(string quote, string source)
{
    var key  = $"{Normalise(quote)}|{Normalise(source)}";
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
    // Force version 4 bits for UUID format validity (cosmetic — still content-derived).
    hash[6] = (byte)((hash[6] & 0x0f) | 0x40);
    hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
    return new Guid(hash[..16]).ToString();
}

static string Normalise(string s) =>
    Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");

// ── Parse vilaboim ───────────────────────────────────────────────────────────
// Format: [ "\"Quote text.\" Movie Title", ... ]

var vilaboims = new List<SeedQuote>();
var vilaboiRaw = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(vilaboim))!;

foreach (var raw in vilaboiRaw)
{
    // Split on the closing quote-mark: "«text»" «source»
    var match = Regex.Match(raw, @"^""(.+?)""\s+(.+)$");
    if (!match.Success)
    {
        Console.Error.WriteLine($"[vilaboim] skipping unparseable: {raw}");
        continue;
    }

    var quote  = match.Groups[1].Value.Trim();
    var source = match.Groups[2].Value.Trim();
    vilaboims.Add(new SeedQuote(StableId(quote, source), quote, source, null, "movie"));
}

Console.WriteLine($"[vilaboim]  parsed {vilaboims.Count} quotes");

// ── Parse NikhilNamal17 ──────────────────────────────────────────────────────
// Format: [ { "quote": "...", "movie": "...", "type": "movie", "year": 1984 }, ... ]

var nikhils = new List<SeedQuote>();
var nikhilDoc = JsonDocument.Parse(File.ReadAllText(nikhil));

foreach (var el in nikhilDoc.RootElement.EnumerateArray())
{
    var quote  = el.TryGetProperty("quote",  out var q) ? q.GetString()?.Trim() : null;
    var source = el.TryGetProperty("movie",  out var m) ? m.GetString()?.Trim() : null;
    var type   = el.TryGetProperty("type",   out var t) ? t.GetString()?.Trim() : null;
    var year   = el.TryGetProperty("year",   out var y) && y.ValueKind == JsonValueKind.Number
                    ? (int?)y.GetInt32() : null;

    if (string.IsNullOrWhiteSpace(quote) || string.IsNullOrWhiteSpace(source))
        continue;

    // Clamp obviously wrong years (the dataset has year=1890 for Star Wars etc.)
    string? date = year is > 1900 and < 2100 ? year.ToString() : null;

    // Normalise type to our canonical set
    var canonicalType = (type?.ToLowerInvariant()) switch
    {
        "movie"  => "movie",
        "tv"     => "tv",
        "anime"  => "anime",
        "book"   => "book",
        "person" => "person",
        _        => "movie"
    };

    nikhils.Add(new SeedQuote(StableId(quote, source), quote, source, date, canonicalType));
}

Console.WriteLine($"[nikhil]    parsed {nikhils.Count} quotes");

// ── Merge and dedup ───────────────────────────────────────────────────────────
// Deduplicate on normalised quote text — keep the richer record (nikhil has year/type).
// NikhilNamal17 goes first so its richer metadata wins when both have the same quote.

var seen = new HashSet<string>();
var merged = new List<SeedQuote>();

foreach (var q in nikhils.Concat(vilaboims))
{
    var key = Normalise(q.Quote);
    if (seen.Add(key))
        merged.Add(q);
}

Console.WriteLine($"[merge]     {merged.Count} unique quotes ({nikhils.Count + vilaboims.Count - merged.Count} duplicates removed)");

// ── Write output ──────────────────────────────────────────────────────────────

var output = merged.Select(q => new Dictionary<string, object?>
{
    ["id"]               = q.Id,
    ["quote"]            = q.Quote,
    ["originalLanguage"] = q.OriginalLanguage,
    ["source"]           = q.Source,
    ["date"]             = q.Date,
    ["character"]        = null,
    ["author"]           = null,
    ["type"]             = q.Type,
    ["genres"]           = Array.Empty<string>(),
    ["translations"]     = new Dictionary<string, object>()
}).ToList();

var opts = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never
};

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, JsonSerializer.Serialize(output, opts));
Console.WriteLine($"[output]    wrote {output.Count} quotes to {outputPath}");
