#!/usr/bin/env dotnet-script
#nullable enable
// Quotinator changelog generator
// Reads src/Quotinator.Api/resources/changelog.json and writes CHANGELOG.md in one of two formats.
//
// Usage (run from repo root):
//   dotnet-script scripts/changelog.csx -- --format keepachangelog --output CHANGELOG.md
//   dotnet-script scripts/changelog.csx -- --format ha-addon        --output addon/CHANGELOG.md
//
// Options:
//   --input              <path>    JSON source file (required)
//   --output             <path>    Destination file path; omit to write to stdout
//   --format             <name>    keepachangelog | ha-addon  (required)
//   --audience           <name>    Audience key for ha-addon format audienceHighlights lookup (default: ha-addon)
//   --fallback           <bool>    ha-addon: when audienceHighlights.<audience> is absent, fall back to
//                                  standard highlights (default: true). Pass false to emit --fallback-message
//                                  instead of standard highlights.
//   --fallback-message   <text>    Message emitted when --fallback false and no audience key is present
//                                  (default: "No user-facing changes.")
//   --lang               <code>    ISO 639-1 language code (default: en)
//                                  Resolves content from translations.<code>.* when available;
//                                  falls back to source language content when translation is absent.
//   --machine-translated <bool>    Default value for machineTranslated on translation items
//                                  that do not specify the property (default: true).
//                                  Pass false when all translations in the JSON were done by hand.
//   --line-endings       <style>   lf | crlf (default: lf)
//                                  Line ending style for the output file.

#r "nuget: System.Text.Json, 8.0.0"

using System.Text;
using System.Text.Json;

// ── CLI arguments ─────────────────────────────────────────────────────────────

var inputArg             = Args.SkipWhile(a => a != "--input").Skip(1).FirstOrDefault();
var outputArg            = Args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault();
var formatArg            = Args.SkipWhile(a => a != "--format").Skip(1).FirstOrDefault();
var audienceArg          = Args.SkipWhile(a => a != "--audience").Skip(1).FirstOrDefault() ?? "ha-addon";
var fallbackArg          = Args.SkipWhile(a => a != "--fallback").Skip(1).FirstOrDefault();
var doFallback           = fallbackArg?.ToLowerInvariant() != "false";
var fallbackMessage      = Args.SkipWhile(a => a != "--fallback-message").Skip(1).FirstOrDefault() ?? "No user-facing changes.";
var langArg              = Args.SkipWhile(a => a != "--lang").Skip(1).FirstOrDefault() ?? "en";
var machineTranslatedArg = Args.SkipWhile(a => a != "--machine-translated").Skip(1).FirstOrDefault();
var defaultMachineTranslated = machineTranslatedArg?.ToLowerInvariant() != "false";
var lineEndingsArg       = Args.SkipWhile(a => a != "--line-endings").Skip(1).FirstOrDefault() ?? "lf";

if (string.IsNullOrEmpty(formatArg) || string.IsNullOrEmpty(inputArg))
{
    Console.Error.WriteLine("Usage: dotnet-script scripts/changelog.csx -- --format <keepachangelog|ha-addon> --input <path> [--output <path>] [--audience <name>] [--fallback <true|false>] [--fallback-message <text>] [--lang <code>] [--machine-translated <true|false>] [--line-endings <lf|crlf>]");
    Environment.Exit(1);
}

var lineEnding = lineEndingsArg.ToLowerInvariant() switch
{
    "lf"   => "\n",
    "crlf" => "\r\n",
    _      => null
};

if (lineEnding is null)
{
    Console.Error.WriteLine($"Unknown --line-endings value '{lineEndingsArg}'. Use lf or crlf.");
    Environment.Exit(1);
}

// ── Paths ─────────────────────────────────────────────────────────────────────

var repoRoot  = Directory.GetCurrentDirectory();
var inputPath = inputArg;

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
var unreleased     = root.TryGetProperty("unreleased", out var u) ? u : (JsonElement?)null;
var sourceLang     = root.TryGetProperty("sourceLanguage", out var sl) ? sl.GetString() ?? "en" : "en";
var sectionHeaders = ParseSectionHeaders(root);

// ── Build regenerate command ──────────────────────────────────────────────────

var cmdBuilder = new StringBuilder("dotnet-script changelog.csx --");
cmdBuilder.Append($" --format {formatArg}");
cmdBuilder.Append($" --input {inputArg}");
if (outputArg is not null)              cmdBuilder.Append($" --output {outputArg}");
if (audienceArg != "ha-addon")          cmdBuilder.Append($" --audience {audienceArg}");
if (!doFallback)
{
    cmdBuilder.Append(" --fallback false");
    if (fallbackMessage != "No user-facing changes.") cmdBuilder.Append($" --fallback-message \"{fallbackMessage}\"");
}
if (langArg != "en")                    cmdBuilder.Append($" --lang {langArg}");
if (!defaultMachineTranslated)          cmdBuilder.Append(" --machine-translated false");
if (lineEndingsArg != "lf")             cmdBuilder.Append($" --line-endings {lineEndingsArg}");
var regenerateCmd = cmdBuilder.ToString();

// ── Generate ──────────────────────────────────────────────────────────────────

var sb     = new StringBuilder();
var format = formatArg.ToLowerInvariant();

if (format == "keepachangelog")
    GenerateKeepAChangelog(sb, releases, unreleased, doFallback, fallbackMessage, langArg, sourceLang, sectionHeaders, inputArg!, regenerateCmd);
else if (format == "ha-addon")
    GenerateHaAddon(sb, releases, audienceArg, doFallback, fallbackMessage, langArg, sourceLang, inputArg!, regenerateCmd);
else
{
    Console.Error.WriteLine($"Unknown format: {formatArg}. Use keepachangelog or ha-addon.");
    Environment.Exit(1);
}

// ── Write output ──────────────────────────────────────────────────────────────

var output = sb.ToString().ReplaceLineEndings("\n").TrimEnd() + "\n";

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

static void GenerateKeepAChangelog(StringBuilder sb, List<JsonElement> releases, JsonElement? unreleased, bool fallback, string fallbackMessage, string lang, string sourceLang, Dictionary<string, Dictionary<string, string>>? sectionHeaders, string inputPath, string regenerateCmd)
{
    // Format must match Quotinator.Changelog.Formatting.GeneratedFileHeader.Build()
    sb.AppendLine(BuildGeneratedHeader(inputPath, regenerateCmd));
    sb.AppendLine();
    sb.AppendLine("# Changelog");
    sb.AppendLine();
    sb.AppendLine("All notable changes to Quotinator are documented here.");
    sb.AppendLine();
    sb.AppendLine("The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).");
    sb.AppendLine();
    sb.AppendLine("---");

    if (unreleased.HasValue)
    {
        var u = unreleased.Value;
        var hasContent = new[] { "highlights", "added", "changed", "fixed", "removed" }
            .Any(key => u.TryGetProperty(key, out var arr) && arr.GetArrayLength() > 0);

        if (hasContent)
        {
            sb.AppendLine();
            sb.AppendLine("## [Unreleased]");

            var unreleasedHighlights = GetHighlights(u, lang, sourceLang, fallback, fallbackMessage);
            if (unreleasedHighlights.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"### {GetSectionHeader("highlights", lang, sourceLang, sectionHeaders)}");
                foreach (var h in unreleasedHighlights)
                    sb.AppendLine($"- {h}");
            }

            AppendSection(sb, u, "added",   lang, sourceLang, sectionHeaders);
            AppendSection(sb, u, "changed", lang, sourceLang, sectionHeaders);
            AppendSection(sb, u, "fixed",   lang, sourceLang, sectionHeaders);
            AppendSection(sb, u, "removed", lang, sourceLang, sectionHeaders);

            sb.AppendLine();
            sb.AppendLine("---");
        }
    }

    for (int i = 0; i < releases.Count; i++)
    {
        var r       = releases[i];
        var version = r.GetProperty("version").GetString()!;
        var date    = r.GetProperty("date").GetString()!;

        sb.AppendLine();
        sb.AppendLine($"## [{version}] - {date}");

        var highlights = GetHighlights(r, lang, sourceLang, fallback, fallbackMessage);
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
    if (unreleased.HasValue && releases.Count > 0)
    {
        var latest = releases[0].GetProperty("version").GetString()!;
        sb.AppendLine($"[Unreleased]: https://github.com/DutchJaFO/Quotinator/compare/v{latest}...HEAD");
    }
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

static void GenerateHaAddon(StringBuilder sb, List<JsonElement> releases, string audience, bool fallback, string fallbackMessage, string lang, string sourceLang, string inputPath, string regenerateCmd)
{
    // Format must match Quotinator.Changelog.Formatting.GeneratedFileHeader.Build()
    sb.AppendLine(BuildGeneratedHeader(inputPath, regenerateCmd));
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

        var highlights = GetAudienceHighlights(r, audience, fallback, fallbackMessage, lang, sourceLang);
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

static List<string> GetHighlights(JsonElement r, string lang, string sourceLang, bool fallback, string fallbackMessage)
{
    var items = GetItems(r, "highlights", lang, sourceLang);
    if (items.Count > 0) return items;
    if (!fallback) return [fallbackMessage];
    return [];
}

static List<string> GetAudienceHighlights(JsonElement r, string audience, bool fallback, string fallbackMessage, string lang, string sourceLang)
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

    // Audience key absent: --fallback false means do not leak standard highlights to this audience.
    if (!fallback)
        return [fallbackMessage];

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

// Format must match Quotinator.Changelog.Formatting.GeneratedFileHeader.Build()
static string BuildGeneratedHeader(string inputPath, string regenerateCmd)
{
    var sb = new StringBuilder();
    sb.AppendLine($"### *GENERATED FILE [{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC] — do not edit by hand.*");
    sb.AppendLine();
    sb.AppendLine($"*Edit: `{inputPath}`*");
    sb.AppendLine();
    sb.Append($"*To regenerate: `{regenerateCmd}`*");
    return sb.ToString();
}
