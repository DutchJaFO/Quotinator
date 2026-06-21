#!/usr/bin/env dotnet-script
#nullable enable
// Quotinator changelog importer
// Parses an existing CHANGELOG.md and produces a changelog.json compatible
// with the Quotinator changelog system. The output can then be round-tripped
// through changelog.csx to verify fidelity.
//
// Usage (run from repo root):
//   dotnet-script scripts/changelog-import.csx -- --format keepachangelog --input scripts/changelog-reference/CHANGELOG.md --output scripts/changelog-reference/changelog-from-reference.json
//   dotnet-script scripts/changelog-import.csx -- --format ha-addon        --input scripts/changelog-reference/addon-CHANGELOG.md
//
// Options:
//   --input           <path>   Source markdown file (required)
//   --output          <path>   Destination JSON file; omit to write to stdout
//   --format          <name>   keepachangelog | ha-addon (required)
//   --highlights-only          Strip added/changed/fixed/removed; keep only highlights
//
// The keepachangelog format recognises ### Highlights, ### Added, ### Changed,
// ### Fixed, ### Removed sections. All other ### headings are ignored.
// The ha-addon format has no section headings — all bullets map to highlights.
// Footer link-reference lines ([version]: url) are silently skipped.
// Section separator lines (---) are silently skipped.

#r "nuget: System.Text.Json, 8.0.0"

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// ── CLI arguments ─────────────────────────────────────────────────────────────

var inputArg        = Args.SkipWhile(a => a != "--input").Skip(1).FirstOrDefault();
var outputArg       = Args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault();
var formatArg       = Args.SkipWhile(a => a != "--format").Skip(1).FirstOrDefault();
var highlightsOnly  = Args.Contains("--highlights-only");

if (string.IsNullOrEmpty(formatArg) || string.IsNullOrEmpty(inputArg))
{
    Console.Error.WriteLine("Usage: dotnet-script scripts/changelog-import.csx -- --format <keepachangelog|ha-addon> --input <path> [--output <path>]");
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

var releases = format == "keepachangelog"
    ? ParseKeepAChangelog(lines)
    : ParseHaAddon(lines);

if (highlightsOnly)
    foreach (var r in releases)
    {
        r.Added.Clear();
        r.Changed.Clear();
        r.Fixed.Clear();
        r.Removed.Clear();
    }

// ── Write JSON ────────────────────────────────────────────────────────────────

var json = ToJson(releases);

if (outputArg is not null)
{
    var outPath = Path.IsPathRooted(outputArg) ? outputArg : Path.Combine(repoRoot, outputArg);
    File.WriteAllText(outPath, json + "\n");
    Console.WriteLine($"Written: {outPath}");
}
else
{
    Console.Write(json);
}

// ── Format parsers ────────────────────────────────────────────────────────────

static List<Release> ParseKeepAChangelog(string[] lines)
{
    var releases = new List<Release>();
    Release? current  = null;
    string?  section  = null;

    var reVersion = new Regex(@"^## \[(.+?)\] - (\d{4}(?:-\d{2}(?:-\d{2})?)?)");
    var reSection = new Regex(@"^### (.+)$");
    var reBullet  = new Regex(@"^- (.+)$");
    var reLink    = new Regex(@"^\[.+?\]: https?://");

    var knownSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "highlights", "added", "changed", "fixed", "removed" };

    foreach (var raw in lines)
    {
        var line = raw.TrimEnd();

        if (string.IsNullOrEmpty(line) || line == "---" || reLink.IsMatch(line))
            continue;

        var vm = reVersion.Match(line);
        if (vm.Success)
        {
            current = new Release(vm.Groups[1].Value, vm.Groups[2].Value);
            releases.Add(current);
            section = null;
            continue;
        }

        if (current is null) continue;

        var sm = reSection.Match(line);
        if (sm.Success)
        {
            var name = sm.Groups[1].Value.ToLowerInvariant();
            section = knownSections.Contains(name) ? name : null;
            continue;
        }

        var bm = reBullet.Match(line);
        if (!bm.Success) continue;

        var text   = bm.Groups[1].Value;
        var target = section switch
        {
            "highlights" => current.Highlights,
            "added"      => current.Added,
            "changed"    => current.Changed,
            "fixed"      => current.Fixed,
            "removed"    => current.Removed,
            _            => current.Highlights   // no section header yet — treat as highlights
        };
        target.Add(text);
    }

    return releases;
}

static List<Release> ParseHaAddon(string[] lines)
{
    var releases = new List<Release>();
    Release? current = null;

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
            current = new Release(vm.Groups[1].Value, vm.Groups[2].Value);
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

static string ToJson(List<Release> releases)
{
    var options = new JsonWriterOptions { Indented = true };
    var stream  = new MemoryStream();
    var writer  = new Utf8JsonWriter(stream, options);

    writer.WriteStartObject();
    writer.WriteStartArray("releases");
    foreach (var r in releases)
    {
        writer.WriteStartObject();
        writer.WriteString("version", r.Version);
        writer.WriteString("date",    r.Date);
        WriteSection(writer, "highlights", r.Highlights);
        WriteSection(writer, "added",      r.Added);
        WriteSection(writer, "changed",    r.Changed);
        WriteSection(writer, "fixed",      r.Fixed);
        WriteSection(writer, "removed",    r.Removed);
        writer.WriteEndObject();
    }
    writer.WriteEndArray();
    writer.WriteEndObject();

    writer.Flush();
    return Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n");
}

static void WriteSection(Utf8JsonWriter writer, string key, List<string> items)
{
    if (items.Count == 0) return;
    writer.WriteStartArray(key);
    foreach (var item in items) writer.WriteStringValue(item);
    writer.WriteEndArray();
}

record Release(string Version, string Date)
{
    public List<string> Highlights { get; } = [];
    public List<string> Added      { get; } = [];
    public List<string> Changed    { get; } = [];
    public List<string> Fixed      { get; } = [];
    public List<string> Removed    { get; } = [];
}
