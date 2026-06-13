#!/usr/bin/env dotnet-script
#nullable enable
// Quotinator seed script
// Reads sources.json, downloads each source, normalises to the canonical schema,
// deduplicates, and writes data/quotes.json.
//
// Usage:
//   dotnet-script scripts/seed.csx
//
// Options:
//   --dry-run    Print stats without writing data/quotes.json
//   --no-fetch   Use cached files in scripts/cache/ instead of downloading
//
// Adding a new source: see scripts/SOURCES.md

#r "nuget: System.Text.Json, 8.0.0"

using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// ── CLI flags ─────────────────────────────────────────────────────────────────

var dryRun   = Args.Contains("--dry-run");
var noFetch  = Args.Contains("--no-fetch");

// ── Paths ─────────────────────────────────────────────────────────────────────

// Run from the repo root: dotnet-script scripts/seed.csx
var repoRoot    = Directory.GetCurrentDirectory();
var sourcesJson = Path.Combine(repoRoot, "scripts", "sources.json");
var cacheDir    = Path.Combine(repoRoot, "scripts", "cache");
var outputPath  = Path.Combine(repoRoot, "data", "quotes.json");

Directory.CreateDirectory(cacheDir);

// ── Source config ─────────────────────────────────────────────────────────────

var sources = JsonNode.Parse(File.ReadAllText(sourcesJson))!.AsArray();

// ── Helpers ───────────────────────────────────────────────────────────────────

static string Normalise(string s) =>
    Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");

// Deterministic UUID: SHA-256 of normalised quote|source → first 16 bytes.
static string StableId(string quote, string source)
{
    var key  = $"{Normalise(quote)}|{Normalise(source)}";
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
    hash[6] = (byte)((hash[6] & 0x0f) | 0x40);
    hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
    return new Guid(hash[..16]).ToString();
}

static string? CleanYear(JsonNode? yearNode)
{
    if (yearNode is null) return null;
    if (yearNode.GetValueKind() == JsonValueKind.Number)
    {
        var y = yearNode.GetValue<int>();
        return y is > 1900 and < 2100 ? y.ToString() : null;
    }
    var s = yearNode.GetValue<string>()?.Trim();
    return int.TryParse(s, out var parsed) && parsed is > 1900 and < 2100 ? parsed.ToString() : null;
}

static string CanonicalType(string? raw, string defaultType) => raw?.ToLowerInvariant() switch
{
    "movie"  => "movie",
    "tv"     => "tv",
    "anime"  => "anime",
    "book"   => "book",
    "person" => "person",
    _        => defaultType
};

// ── Adapters ──────────────────────────────────────────────────────────────────

// quoted-string: [ "\"Quote text.\" Movie Title", ... ]
static List<(string Quote, string Source, string? Date, string Type)>
    ParseQuotedString(JsonNode root, string defaultType)
{
    var results = new List<(string, string, string?, string)>();
    foreach (var item in root.AsArray())
    {
        var raw = item?.GetValue<string>();
        if (raw is null) continue;
        var m = Regex.Match(raw, @"^""(.+?)""\s+(.+)$");
        if (!m.Success)
        {
            Console.Error.WriteLine($"  [skip] unparseable: {raw}");
            continue;
        }
        results.Add((m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim(), null, defaultType));
    }
    return results;
}

// object-array: [ { "quote": "...", "movie": "...", ... }, ... ]
// Fields are remapped via sources.json fieldMap.
static List<(string Quote, string Source, string? Date, string Type)>
    ParseObjectArray(JsonNode root, JsonNode fieldMap, string defaultType)
{
    var results = new List<(string, string, string?, string)>();
    var qField = fieldMap["quote"]?.GetValue<string>()  ?? "quote";
    var sField = fieldMap["source"]?.GetValue<string>() ?? "source";
    var tField = fieldMap["type"]?.GetValue<string>()   ?? "type";
    var yField = fieldMap["year"]?.GetValue<string>()   ?? "year";

    foreach (var item in root.AsArray())
    {
        if (item is null) continue;
        var quote  = item[qField]?.GetValue<string>()?.Trim();
        var source = item[sField]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(quote) || string.IsNullOrWhiteSpace(source)) continue;

        var type = CanonicalType(item[tField]?.GetValue<string>(), defaultType);
        var date = CleanYear(item[yField]);
        results.Add((quote, source, date, type));
    }
    return results;
}

// ── Main ──────────────────────────────────────────────────────────────────────

var allQuotes = new List<(string Quote, string Source, string? Date, string Type)>();

var http = new HttpClient();
http.DefaultRequestHeaders.Add("User-Agent", "Quotinator-seed/1.0");

foreach (var src in sources)
{
    var name        = src!["name"]!.GetValue<string>();
    var url         = src["url"]!.GetValue<string>();
    var format      = src["format"]!.GetValue<string>();
    var defaultType = src["defaultType"]?.GetValue<string>() ?? "movie";
    var cacheFile   = Path.Combine(cacheDir, $"{name.Replace("/", "_")}.json");

    Console.WriteLine($"\n[{name}]");

    string json;
    if (noFetch && File.Exists(cacheFile))
    {
        Console.WriteLine($"  using cache: {cacheFile}");
        json = File.ReadAllText(cacheFile);
    }
    else
    {
        Console.Write($"  fetching {url} ... ");
        json = http.GetStringAsync(url).GetAwaiter().GetResult();
        File.WriteAllText(cacheFile, json);
        Console.WriteLine("done");
    }

    var root = JsonNode.Parse(json)!;
    var parsed = format switch
    {
        "quoted-string" => ParseQuotedString(root, defaultType),
        "object-array"  => ParseObjectArray(root, src["fieldMap"]!, defaultType),
        _               => throw new InvalidOperationException($"Unknown format: {format}")
    };

    Console.WriteLine($"  parsed {parsed.Count} quotes");
    allQuotes.AddRange(parsed);
}

http.Dispose();

// ── Merge and dedup ───────────────────────────────────────────────────────────

Console.WriteLine($"\n[merge]");
Console.WriteLine($"  total before dedup: {allQuotes.Count}");

var seen   = new HashSet<string>();
var merged = new List<(string Quote, string Source, string? Date, string Type)>();

foreach (var q in allQuotes)
{
    if (seen.Add(Normalise(q.Quote)))
        merged.Add(q);
}

Console.WriteLine($"  duplicates removed: {allQuotes.Count - merged.Count}");
Console.WriteLine($"  unique quotes:      {merged.Count}");

// ── Output ────────────────────────────────────────────────────────────────────

if (dryRun)
{
    Console.WriteLine("\n[dry-run] skipping write.");
}
else
{
    var output = merged.Select(q => new Dictionary<string, object?>
    {
        ["id"]               = StableId(q.Quote, q.Source),
        ["quote"]            = q.Quote,
        ["originalLanguage"] = "en",
        ["source"]           = q.Source,
        ["date"]             = q.Date,
        ["character"]        = null,
        ["author"]           = null,
        ["type"]             = q.Type,
        ["genres"]           = Array.Empty<string>(),
        ["translations"]     = new Dictionary<string, object>()
    }).ToList();

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    var opts = new JsonSerializerOptions { WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    File.WriteAllText(outputPath, JsonSerializer.Serialize(output, opts));
    Console.WriteLine($"\n[output]  wrote {output.Count} quotes → {outputPath}");
}
