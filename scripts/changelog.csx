#!/usr/bin/env dotnet-script
#nullable enable
// Quotinator changelog generator
// Reads src/Quotinator.Api/changelog.json and writes CHANGELOG.md in one of two formats.
//
// Usage (run from repo root):
//   dotnet-script scripts/changelog.csx -- --format keepachangelog --output CHANGELOG.md
//   dotnet-script scripts/changelog.csx -- --format ha-addon        --output addon/CHANGELOG.md
//
// Options:
//   --input              <path>    JSON source file (default: src/Quotinator.Api/changelog.json)
//   --output             <path>    Destination file path; omit to write to stdout
//   --format             <name>    keepachangelog | ha-addon  (required)
//   --lang               <code>    ISO 639-1 language code (default: en)
//                                  Resolves content from translations.<code>.* when available;
//                                  falls back to source language content when translation is absent.
//   --machine-translated <bool>    Default value for machineTranslated on translation items
//                                  that do not specify the property (default: true).
//                                  Pass false when all translations in the JSON were done by hand.

#r "nuget: System.Text.Json, 8.0.0"

using System.Text;
using System.Text.Json;

// ── CLI arguments ─────────────────────────────────────────────────────────────

var inputArg             = Args.SkipWhile(a => a != "--input").Skip(1).FirstOrDefault();
var outputArg            = Args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault();
var formatArg            = Args.SkipWhile(a => a != "--format").Skip(1).FirstOrDefault();
var langArg              = Args.SkipWhile(a => a != "--lang").Skip(1).FirstOrDefault() ?? "en";
var machineTranslatedArg = Args.SkipWhile(a => a != "--machine-translated").Skip(1).FirstOrDefault();
var defaultMachineTranslated = machineTranslatedArg?.ToLowerInvariant() != "false";

if (string.IsNullOrEmpty(formatArg))
{
    Console.Error.WriteLine("Usage: dotnet-script scripts/changelog.csx -- --format <keepachangelog|ha-addon> [--output <path>] [--input <path>] [--lang <code>] [--machine-translated <true|false>]");
    Environment.Exit(1);
}

// ── Paths ─────────────────────────────────────────────────────────────────────

var repoRoot  = Directory.GetCurrentDirectory();
var inputPath = inputArg ?? Path.Combine(repoRoot, "src", "Quotinator.Api", "changelog.json");

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Input file not found: {inputPath}");
    Environment.Exit(1);
}

// ── Parse JSON ────────────────────────────────────────────────────────────────

var json           = File.ReadAllText(inputPath);
var doc            = JsonDocument.Parse(json);
var root           = doc.RootElement;
var releases       = root.GetProperty("releases").EnumerateArray().ToList();
var sourceLang     = root.TryGetProperty("sourceLanguage", out var sl) ? sl.GetString() ?? "en" : "en";
var sectionHeaders = ParseSectionHeaders(root);

// ── Generate ──────────────────────────────────────────────────────────────────

var sb     = new StringBuilder();
var format = formatArg.ToLowerInvariant();

if (format == "keepachangelog")
    GenerateKeepAChangelog(sb, releases, langArg, sourceLang, sectionHeaders);
else if (format == "ha-addon")
    GenerateHaAddon(sb, releases, langArg, sourceLang);
else
{
    Console.Error.WriteLine($"Unknown format: {formatArg}. Use keepachangelog or ha-addon.");
    Environment.Exit(1);
}

// ── Write output ──────────────────────────────────────────────────────────────

var output = sb.ToString().TrimEnd() + Environment.NewLine;

if (outputArg is not null)
{
    var outPath = Path.IsPathRooted(outputArg) ? outputArg : Path.Combine(repoRoot, outputArg);
    File.WriteAllText(outPath, output);
    Console.WriteLine($"Written: {outPath}");
}
else
{
    Console.Write(output);
}

// ── Format implementations ────────────────────────────────────────────────────

static void GenerateKeepAChangelog(StringBuilder sb, List<JsonElement> releases, string lang, string sourceLang, Dictionary<string, Dictionary<string, string>>? sectionHeaders)
{
    sb.AppendLine("# GENERATED FILE — do not edit by hand. Edit src/Quotinator.Api/changelog.json and run scripts/changelog.csx");
    sb.AppendLine();
    sb.AppendLine("# Changelog");
    sb.AppendLine();
    sb.AppendLine("All notable changes to Quotinator are documented here.");
    sb.AppendLine();
    sb.AppendLine("The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).");
    sb.AppendLine();
    sb.AppendLine("---");

    for (int i = 0; i < releases.Count; i++)
    {
        var r       = releases[i];
        var version = r.GetProperty("version").GetString()!;
        var date    = r.GetProperty("date").GetString()!;

        sb.AppendLine();
        sb.AppendLine($"## [{version}] - {date}");

        var highlights = GetItems(r, "highlights", lang, sourceLang);
        if (highlights.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"### {GetSectionHeader("highlights", lang, sourceLang, sectionHeaders)}");
            foreach (var h in highlights)
                sb.AppendLine($"- {h}");
        }

        AppendSection(sb, r, "added",   lang, sourceLang, sectionHeaders);
        AppendSection(sb, r, "changed", lang, sourceLang, sectionHeaders);
        AppendSection(sb, r, "fixed",   lang, sourceLang, sectionHeaders);
        AppendSection(sb, r, "removed", lang, sourceLang, sectionHeaders);

        if (i < releases.Count - 1)
        {
            sb.AppendLine();
            sb.AppendLine("---");
        }
    }

    // Footer comparison links
    sb.AppendLine();
    for (int i = 0; i < releases.Count; i++)
    {
        var version = releases[i].GetProperty("version").GetString()!;
        string url;
        if (i == releases.Count - 1)
        {
            url = $"https://github.com/DutchJaFO/Quotinator/releases/tag/v{version}";
        }
        else
        {
            var prev = releases[i + 1].GetProperty("version").GetString()!;
            url = $"https://github.com/DutchJaFO/Quotinator/compare/v{prev}...v{version}";
        }
        sb.AppendLine($"[{version}]: {url}");
    }
}

static void GenerateHaAddon(StringBuilder sb, List<JsonElement> releases, string lang, string sourceLang)
{
    sb.AppendLine("# GENERATED FILE — do not edit by hand. Edit src/Quotinator.Api/changelog.json and run scripts/changelog.csx");
    sb.AppendLine();
    sb.AppendLine("# Changelog");
    sb.AppendLine();
    sb.AppendLine("All notable changes to this add-on will be documented in this file.");
    sb.AppendLine();
    sb.AppendLine("The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/).");

    for (int i = 0; i < releases.Count; i++)
    {
        var r       = releases[i];
        var version = r.GetProperty("version").GetString()!;
        var date    = r.GetProperty("date").GetString()!;

        sb.AppendLine();
        sb.AppendLine($"## [{version}] - {date}");

        var highlights = GetAudienceHighlights(r, "ha-addon", lang, sourceLang);
        if (highlights.Count > 0)
        {
            sb.AppendLine();
            foreach (var h in highlights)
                sb.AppendLine($"- {h}");
        }

        if (i < releases.Count - 1)
        {
            sb.AppendLine();
            sb.AppendLine("---");
        }
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static List<string> GetItems(JsonElement r, string key, string lang, string sourceLang)
{
    if (lang == sourceLang)
        return GetTopLevelItems(r, key);

    if (r.TryGetProperty("translations", out var trans) &&
        trans.TryGetProperty(lang, out var langTrans) &&
        langTrans.TryGetProperty(key, out var transItems))
    {
        var items = transItems.EnumerateArray()
            .Select(GetItemText)
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .ToList();
        if (items.Count > 0) return items;
    }

    return GetTopLevelItems(r, key);
}

static List<string> GetAudienceHighlights(JsonElement r, string audience, string lang, string sourceLang)
{
    if (r.TryGetProperty("audienceHighlights", out var audienceHighlights) &&
        audienceHighlights.TryGetProperty(audience, out var audienceItems))
    {
        if (audienceItems.GetArrayLength() == 0)
            return ["No user-facing changes."];

        return audienceItems.EnumerateArray()
            .Select(i => i.GetString() ?? "")
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    return GetItems(r, "highlights", lang, sourceLang);
}

static List<string> GetTopLevelItems(JsonElement r, string key)
{
    if (!r.TryGetProperty(key, out var arr)) return [];
    return arr.EnumerateArray()
        .Select(i => i.GetString() ?? "")
        .Where(s => !string.IsNullOrEmpty(s))
        .ToList();
}

static string? GetItemText(JsonElement item)
{
    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var text))
        return text.GetString();
    return item.GetString();
}

static string GetSectionHeader(string key, string lang, string sourceLang, Dictionary<string, Dictionary<string, string>>? sectionHeaders)
{
    var builtin = key switch
    {
        "highlights" => "Highlights",
        "added"      => "Added",
        "changed"    => "Changed",
        "fixed"      => "Fixed",
        "removed"    => "Removed",
        _            => key.Length > 0 ? char.ToUpper(key[0]) + key[1..] : key
    };

    if (sectionHeaders is null) return builtin;

    if (sectionHeaders.TryGetValue(lang, out var langHeaders) &&
        langHeaders.TryGetValue(key, out var header) &&
        !string.IsNullOrEmpty(header))
        return header;

    if (sectionHeaders.TryGetValue(sourceLang, out var sourceHeaders) &&
        sourceHeaders.TryGetValue(key, out var sourceHeader) &&
        !string.IsNullOrEmpty(sourceHeader))
        return sourceHeader;

    return builtin;
}

static void AppendSection(StringBuilder sb, JsonElement r, string key, string lang, string sourceLang, Dictionary<string, Dictionary<string, string>>? sectionHeaders)
{
    var items = GetItems(r, key, lang, sourceLang);
    if (items.Count == 0) return;

    sb.AppendLine();
    sb.AppendLine($"### {GetSectionHeader(key, lang, sourceLang, sectionHeaders)}");
    foreach (var item in items)
        sb.AppendLine($"- {item}");
}

static Dictionary<string, Dictionary<string, string>>? ParseSectionHeaders(JsonElement root)
{
    if (!root.TryGetProperty("sectionHeaders", out var headersEl)) return null;
    var result = new Dictionary<string, Dictionary<string, string>>();
    foreach (var langEntry in headersEl.EnumerateObject())
    {
        var langDict = new Dictionary<string, string>();
        foreach (var sectionEntry in langEntry.Value.EnumerateObject())
            langDict[sectionEntry.Name] = sectionEntry.Value.GetString() ?? "";
        result[langEntry.Name] = langDict;
    }
    return result;
}
