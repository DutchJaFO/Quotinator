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
//   --input  <path>   JSON source file (default: src/Quotinator.Api/changelog.json)
//   --output <path>   Destination file path; omit to write to stdout
//   --format <name>   keepachangelog | ha-addon  (required)
//   --lang   <code>   ISO 639-1 language code (default: en)
//                     Resolves highlights from translations.<code>.highlights when available

#r "nuget: System.Text.Json, 8.0.0"

using System.Text;
using System.Text.Json;

// ── CLI arguments ─────────────────────────────────────────────────────────────

var inputArg  = Args.SkipWhile(a => a != "--input").Skip(1).FirstOrDefault();
var outputArg = Args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault();
var formatArg = Args.SkipWhile(a => a != "--format").Skip(1).FirstOrDefault();
var langArg   = Args.SkipWhile(a => a != "--lang").Skip(1).FirstOrDefault() ?? "en";

if (string.IsNullOrEmpty(formatArg))
{
    Console.Error.WriteLine("Usage: dotnet-script scripts/changelog.csx -- --format <keepachangelog|ha-addon> [--output <path>] [--input <path>] [--lang <code>]");
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

var json = File.ReadAllText(inputPath);
var doc      = JsonDocument.Parse(json);
var releases = doc.RootElement.GetProperty("releases").EnumerateArray().ToList();

// ── Generate ──────────────────────────────────────────────────────────────────

var sb = new StringBuilder();
var format = formatArg.ToLowerInvariant();

if (format == "keepachangelog")
    GenerateKeepAChangelog(sb, releases, langArg);
else if (format == "ha-addon")
    GenerateHaAddon(sb, releases, langArg);
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

static void GenerateKeepAChangelog(StringBuilder sb, List<JsonElement> releases, string lang)
{
    var langNote = lang != "en" ? $" ({lang})" : "";
    sb.AppendLine($"<!-- GENERATED FILE{langNote} — edit src/Quotinator.Api/changelog.json and run scripts/changelog.csx -->");
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

        var highlights = GetHighlights(r, lang);
        if (highlights.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Highlights");
            foreach (var h in highlights)
                sb.AppendLine($"- {h}");
        }

        AppendSection(sb, r, "added",   "### Added");
        AppendSection(sb, r, "changed", "### Changed");
        AppendSection(sb, r, "fixed",   "### Fixed");
        AppendSection(sb, r, "removed", "### Removed");

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

static void GenerateHaAddon(StringBuilder sb, List<JsonElement> releases, string lang)
{
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

        var highlights = GetHighlights(r, lang);
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

static List<string> GetHighlights(JsonElement r, string lang)
{
    if (lang != "en" &&
        r.TryGetProperty("translations", out var trans) &&
        trans.TryGetProperty(lang, out var langTrans) &&
        langTrans.TryGetProperty("highlights", out var transHighlights))
    {
        var translated = transHighlights.EnumerateArray()
            .Select(h => h.GetString() ?? "")
            .Where(h => !string.IsNullOrEmpty(h))
            .ToList();
        if (translated.Count > 0) return translated;
    }

    if (r.TryGetProperty("highlights", out var highlights))
        return highlights.EnumerateArray()
            .Select(h => h.GetString() ?? "")
            .Where(h => !string.IsNullOrEmpty(h))
            .ToList();

    return [];
}

static void AppendSection(StringBuilder sb, JsonElement r, string key, string header)
{
    if (!r.TryGetProperty(key, out var arr) || arr.GetArrayLength() == 0) return;

    sb.AppendLine();
    sb.AppendLine(header);
    foreach (var item in arr.EnumerateArray())
    {
        var s = item.GetString();
        if (!string.IsNullOrEmpty(s))
            sb.AppendLine($"- {s}");
    }
}
