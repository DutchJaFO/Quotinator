#!/usr/bin/env dotnet-script
#nullable enable
// Quotinator changelog importer
// Parses an existing CHANGELOG.md and produces a changelog.*.json compatible
// with the Quotinator changelog system. The output can then be round-tripped
// through changelog.csx to verify fidelity.
//
// Usage (run from repo root):
//   dotnet-script scripts/changelog-import.csx -- --format keepachangelog --input scripts/changelog-reference/CHANGELOG.md --output scripts/changelog-reference/changelog-from-reference.json
//   dotnet-script scripts/changelog-import.csx -- --format ha-addon        --input scripts/changelog-reference/addon-CHANGELOG.md
//
// Options:
//   --input              <path>    Source markdown file (required)
//   --output             <path>    Destination JSON file; omit to write to stdout
//   --format             <name>    keepachangelog | ha-addon (required)
//   --language           <code>    ISO 639-1 language code for the output file (default: en)
//   --source-language    <code>    ISO 639-1 fallback language code (default: en)
//   --machine-translated <bool>    Whether the content is machine-translated (default: false)
//   --highlights-only              Strip added/changed/fixed/removed; keep only highlights
//   --line-endings       <style>   lf | crlf (default: lf)
//
// The keepachangelog format recognises ### Highlights, ### Added, ### Changed,
// ### Fixed, ### Removed sections. All other ### headings are ignored.
// The ha-addon format has no section headings — all bullets map to highlights.
// Footer link-reference lines ([version]: url) are silently skipped.
// Section separator lines (---) are silently skipped.

#r "../src/Quotinator.Changelog/bin/Release/net10.0/Quotinator.Changelog.dll"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"

using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Quotinator.Changelog.Models;

// ── CLI arguments ─────────────────────────────────────────────────────────────

var inputArg             = Args.SkipWhile(a => a != "--input").Skip(1).FirstOrDefault();
var outputArg            = Args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault();
var formatArg            = Args.SkipWhile(a => a != "--format").Skip(1).FirstOrDefault();
var languageArg          = Args.SkipWhile(a => a != "--language").Skip(1).FirstOrDefault() ?? "en";
var sourceLanguageArg    = Args.SkipWhile(a => a != "--source-language").Skip(1).FirstOrDefault() ?? "en";
var machineTranslatedArg = Args.SkipWhile(a => a != "--machine-translated").Skip(1).FirstOrDefault();
var machineTranslated    = machineTranslatedArg?.ToLowerInvariant() == "true";
var highlightsOnly       = Args.Contains("--highlights-only");
var lineEndingsArg       = Args.SkipWhile(a => a != "--line-endings").Skip(1).FirstOrDefault() ?? "lf";

if (string.IsNullOrEmpty(formatArg) || string.IsNullOrEmpty(inputArg))
{
    Console.Error.WriteLine("Usage: dotnet-script scripts/changelog-import.csx -- --format <keepachangelog|ha-addon> --input <path> [--output <path>] [--language <code>] [--source-language <code>] [--machine-translated <true|false>] [--line-endings <lf|crlf>]");
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

var repoRoot = Directory.GetCurrentDirectory();
var inPath   = Path.IsPathRooted(inputArg) ? inputArg : Path.Combine(repoRoot, inputArg);

if (!File.Exists(inPath))
{
    Console.Error.WriteLine($"Input file not found: {inPath}");
    Environment.Exit(1);
}

// ── Parse ─────────────────────────────────────────────────────────────────────

var lines  = File.ReadAllLines(inPath);
var format = formatArg.ToLowerInvariant();

if (format != "keepachangelog" && format != "ha-addon")
{
    Console.Error.WriteLine($"Unknown format: {formatArg}. Use keepachangelog or ha-addon.");
    Environment.Exit(1);
}

var (unreleased, releases) = format == "keepachangelog"
    ? ParseKeepAChangelog(lines)
    : (null, ParseHaAddon(lines));

// CVE IDs are detected automatically from all section text before any stripping.
// Issue numbers are not auto-detected — they are ambiguous (#N could be a PR, footnote, etc.)
// and must be added manually to the issues array.
if (unreleased is not null)
{
    var cves = ExtractCves(AllText(unreleased));
    if (cves is not null) unreleased.Cves.AddRange(cves);
}
foreach (var r in releases)
{
    var cves = ExtractCves(AllText(r));
    if (cves is not null) r.Cves.AddRange(cves);
}

if (highlightsOnly)
{
    unreleased?.Added.Clear();
    unreleased?.Changed.Clear();
    unreleased?.Fixed.Clear();
    unreleased?.Removed.Clear();
    foreach (var r in releases)
    {
        r.Added.Clear();
        r.Changed.Clear();
        r.Fixed.Clear();
        r.Removed.Clear();
    }
}

// ── Write JSON ────────────────────────────────────────────────────────────────

var json = ToJson(unreleased, releases, languageArg, sourceLanguageArg, machineTranslated, lineEnding!);

if (outputArg is not null)
{
    var outPath = Path.IsPathRooted(outputArg) ? outputArg : Path.Combine(repoRoot, outputArg);
    File.WriteAllText(outPath, json + lineEnding);
    Console.WriteLine($"Written: {outPath}");
}
else
{
    Console.Write(json);
}

// ── Format parsers ────────────────────────────────────────────────────────────

static (ChangelogUnreleased? unreleased, List<ChangelogRelease> releases) ParseKeepAChangelog(string[] lines)
{
    var releases             = new List<ChangelogRelease>();
    ChangelogRelease?    current      = null;
    ChangelogUnreleased? unreleased   = null;
    string?              section      = null;
    bool                 inUnreleased = false;

    var reVersion    = new Regex(@"^## \[(.+?)\] - (\d{4}(?:-\d{2}(?:-\d{2})?)?)");
    var reUnreleased = new Regex(@"^## \[Unreleased\]$", RegexOptions.IgnoreCase);
    var reSection    = new Regex(@"^### (.+)$");
    var reBullet     = new Regex(@"^- (.+)$");
    var reLink       = new Regex(@"^\[.+?\]: https?://");

    var knownSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "highlights", "added", "changed", "fixed", "removed" };

    foreach (var raw in lines)
    {
        var line = raw.TrimEnd();

        if (string.IsNullOrEmpty(line) || line == "---" || reLink.IsMatch(line))
            continue;

        if (reUnreleased.IsMatch(line))
        {
            unreleased   = new ChangelogUnreleased();
            inUnreleased = true;
            current      = null;
            section      = null;
            continue;
        }

        var vm = reVersion.Match(line);
        if (vm.Success)
        {
            current      = new ChangelogRelease { Version = vm.Groups[1].Value, Date = vm.Groups[2].Value };
            inUnreleased = false;
            releases.Add(current);
            section = null;
            continue;
        }

        if (!inUnreleased && current is null) continue;

        var sm = reSection.Match(line);
        if (sm.Success)
        {
            var name = sm.Groups[1].Value.ToLowerInvariant();
            section = knownSections.Contains(name) ? name : null;
            continue;
        }

        var bm = reBullet.Match(line);
        if (!bm.Success) continue;

        var text = bm.Groups[1].Value;

        if (inUnreleased && unreleased is not null)
        {
            var target = section switch
            {
                "highlights" => unreleased.Highlights,
                "added"      => unreleased.Added,
                "changed"    => unreleased.Changed,
                "fixed"      => unreleased.Fixed,
                "removed"    => unreleased.Removed,
                _            => unreleased.Highlights
            };
            target.Add(text);
        }
        else if (current is not null)
        {
            var target = section switch
            {
                "highlights" => current.Highlights,
                "added"      => current.Added,
                "changed"    => current.Changed,
                "fixed"      => current.Fixed,
                "removed"    => current.Removed,
                _            => current.Highlights
            };
            target.Add(text);
        }
    }

    return (unreleased, releases);
}

static List<ChangelogRelease> ParseHaAddon(string[] lines)
{
    var releases = new List<ChangelogRelease>();
    ChangelogRelease? current = null;

    var reVersion = new Regex(@"^## \[(.+?)\] - (\d{4}(?:-\d{2}(?:-\d{2})?)?)");
    var reBullet  = new Regex(@"^- (.+)$");

    foreach (var raw in lines)
    {
        var line = raw.TrimEnd();

        if (string.IsNullOrEmpty(line) || line == "---")
            continue;

        var vm = reVersion.Match(line);
        if (vm.Success)
        {
            current = new ChangelogRelease { Version = vm.Groups[1].Value, Date = vm.Groups[2].Value };
            releases.Add(current);
            continue;
        }

        if (current is null) continue;

        var bm = reBullet.Match(line);
        if (bm.Success)
            current.Highlights.Add(bm.Groups[1].Value);
    }

    return releases;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static string ToJson(ChangelogUnreleased? unreleased, List<ChangelogRelease> releases, string language, string sourceLanguage, bool machineTranslated, string lineEnding)
{
    var options = new JsonSerializerOptions
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver     = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { SkipEmptyOrNull }
        }
    };
    var root = new ChangelogRoot
    {
        Language          = language,
        SourceLanguage    = sourceLanguage,
        MachineTranslated = machineTranslated,
        Unreleased        = unreleased,
        Releases          = releases
    };
    return JsonSerializer.Serialize(root, options).ReplaceLineEndings(lineEnding);
}

// Skips null properties and empty collections from JSON output.
// Keeps output clean without requiring [JsonIgnore] on the project models.
static void SkipEmptyOrNull(JsonTypeInfo typeInfo)
{
    foreach (var prop in typeInfo.Properties)
    {
        var original = prop.ShouldSerialize;
        prop.ShouldSerialize = (obj, val) =>
        {
            if (val is null) return false;
            if (val is ICollection c && c.Count == 0) return false;
            return original is null || original(obj, val);
        };
    }
}

static IEnumerable<string> AllText(ChangelogUnreleased entry) =>
    new[] { entry.Highlights, entry.Added, entry.Changed, entry.Fixed, entry.Removed }
        .SelectMany(l => l);

static readonly Regex CvePattern = new(@"CVE-\d{4}-\d{4,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

static List<string>? ExtractCves(IEnumerable<string> text)
{
    var cves = text
        .SelectMany(t => CvePattern.Matches(t).Select(m => m.Value.ToUpperInvariant()))
        .Distinct()
        .OrderBy(c => c)
        .ToList();
    return cves.Count > 0 ? cves : null;
}
