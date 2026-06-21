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
//   --input           <path>    Source markdown file (required)
//   --output          <path>    Destination JSON file; omit to write to stdout
//   --format          <name>    keepachangelog | ha-addon (required)
//   --highlights-only           Strip added/changed/fixed/removed; keep only highlights
//   --line-endings    <style>   lf | crlf (default: lf)
//
// The keepachangelog format recognises ### Highlights, ### Added, ### Changed,
// ### Fixed, ### Removed sections. All other ### headings are ignored.
// The ha-addon format has no section headings — all bullets map to highlights.
// Footer link-reference lines ([version]: url) are silently skipped.
// Section separator lines (---) are silently skipped.

#r "nuget: System.Text.Json, 8.0.0"

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// ── CLI arguments ─────────────────────────────────────────────────────────────

var inputArg        = Args.SkipWhile(a => a != "--input").Skip(1).FirstOrDefault();
var outputArg       = Args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault();
var formatArg       = Args.SkipWhile(a => a != "--format").Skip(1).FirstOrDefault();
var highlightsOnly  = Args.Contains("--highlights-only");
var lineEndingsArg  = Args.SkipWhile(a => a != "--line-endings").Skip(1).FirstOrDefault() ?? "lf";

if (string.IsNullOrEmpty(formatArg) || string.IsNullOrEmpty(inputArg))
{
    Console.Error.WriteLine("Usage: dotnet-script scripts/changelog-import.csx -- --format <keepachangelog|ha-addon> --input <path> [--output <path>] [--line-endings <lf|crlf>]");
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
if (unreleased is not null) unreleased.Cves = ExtractCves(unreleased.AllText());
foreach (var r in releases) r.Cves = ExtractCves(r.AllText());

if (highlightsOnly)
{
    if (unreleased is not null)
    {
        unreleased.Added   = null;
        unreleased.Changed = null;
        unreleased.Fixed   = null;
        unreleased.Removed = null;
    }
    foreach (var r in releases)
    {
        r.Added   = null;
        r.Changed = null;
        r.Fixed   = null;
        r.Removed = null;
    }
}

// ── Write JSON ────────────────────────────────────────────────────────────────

var json = ToJson(unreleased, releases, lineEnding);

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

static (UnreleasedEntry? unreleased, List<Release> releases) ParseKeepAChangelog(string[] lines)
{
    var releases   = new List<Release>();
    Release?          current    = null;
    UnreleasedEntry?  unreleased = null;
    string?           section    = null;
    bool              inUnreleased = false;

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
            unreleased   = new UnreleasedEntry();
            inUnreleased = true;
            current      = null;
            section      = null;
            continue;
        }

        var vm = reVersion.Match(line);
        if (vm.Success)
        {
            current      = new Release(vm.Groups[1].Value, vm.Groups[2].Value);
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
                "highlights" => unreleased.Highlights ??= [],
                "added"      => unreleased.Added      ??= [],
                "changed"    => unreleased.Changed    ??= [],
                "fixed"      => unreleased.Fixed      ??= [],
                "removed"    => unreleased.Removed    ??= [],
                _            => unreleased.Highlights ??= []
            };
            target.Add(text);
        }
        else if (current is not null)
        {
            var target = section switch
            {
                "highlights" => current.Highlights ??= [],
                "added"      => current.Added      ??= [],
                "changed"    => current.Changed    ??= [],
                "fixed"      => current.Fixed      ??= [],
                "removed"    => current.Removed    ??= [],
                _            => current.Highlights ??= []
            };
            target.Add(text);
        }
    }

    return (unreleased, releases);
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
            (current.Highlights ??= []).Add(bm.Groups[1].Value);
    }

    return releases;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static string ToJson(UnreleasedEntry? unreleased, List<Release> releases, string lineEnding)
{
    var options = new JsonSerializerOptions
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    var doc = new ChangelogDocument { Unreleased = unreleased, Releases = releases };
    return JsonSerializer.Serialize(doc, options).ReplaceLineEndings(lineEnding);
}

record ChangelogDocument
{
    [JsonPropertyOrder(0)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UnreleasedEntry? Unreleased { get; init; }

    [JsonPropertyOrder(1)]
    public List<Release> Releases { get; init; } = [];
}

record UnreleasedEntry
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Highlights { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Added { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Changed { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Fixed { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Removed { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Cves { get; set; }

    public IEnumerable<string> AllText() =>
        new[] { Highlights, Added, Changed, Fixed, Removed }
            .Where(l => l is not null).SelectMany(l => l!);
}

record Release(
    [property: JsonPropertyOrder(0)] string Version,
    [property: JsonPropertyOrder(1)] string Date)
{
    [JsonPropertyOrder(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Cves { get; set; }

    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Highlights { get; set; }

    [JsonPropertyOrder(4)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Added { get; set; }

    [JsonPropertyOrder(5)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Changed { get; set; }

    [JsonPropertyOrder(6)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Fixed { get; set; }

    [JsonPropertyOrder(7)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Removed { get; set; }

    public IEnumerable<string> AllText() =>
        new[] { Highlights, Added, Changed, Fixed, Removed }
            .Where(l => l is not null).SelectMany(l => l!);
}

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
