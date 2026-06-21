#!/usr/bin/env dotnet-script
#nullable enable
// Quotinator changelog build script
// Assembles target-changelog.json from the two reference changelogs.
//
// Usage (run from repo root):
//   dotnet-script scripts/changelog-build.csx
//
// Reads from:
//   scripts/changelog-reference/CHANGELOG.md
//   scripts/changelog-reference/addon-CHANGELOG.md
//
// Writes intermediate files:
//   scripts/changelog-reference/changelog-root.json       (full keepachangelog import)
//   scripts/changelog-reference/changelog-highlights.json (highlights-only keepachangelog import)
//   scripts/changelog-reference/changelog-ha-addon.json   (ha-addon import)
//
// Writes final output:
//   scripts/changelog-reference/target-changelog.json
//
// Assembly rules for target-changelog.json:
//   - Base structure comes from changelog-root.json, without the highlights section
//   - highlights field  ← ha-addon bullets for matching versions
//   - audienceHighlights.ha-addon ← root highlights for matching versions
//   - Versions present in ha-addon but absent from root are appended (oldest last)

#r "nuget: System.Text.Json, 8.0.0"

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

var repoRoot  = Directory.GetCurrentDirectory();
var refDir    = Path.Combine(repoRoot, "scripts", "changelog-reference");
var importCsx = Path.Combine(repoRoot, "scripts", "changelog-import.csx");

var rootMd    = Path.Combine(refDir, "CHANGELOG.md");
var addonMd   = Path.Combine(refDir, "addon-CHANGELOG.md");

var rootJson       = Path.Combine(refDir, "changelog-root.json");
var highlightsJson = Path.Combine(refDir, "changelog-highlights.json");
var haAddonJson    = Path.Combine(refDir, "changelog-ha-addon.json");
var targetJson     = Path.Combine(refDir, "target-changelog.json");

// ── Steps 1–3: Import source changelogs ───────────────────────────────────────

RunImport(importCsx, "keepachangelog",             rootMd,  rootJson);
RunImport(importCsx, "keepachangelog", "--highlights-only", rootMd,  highlightsJson);
RunImport(importCsx, "ha-addon",                   addonMd, haAddonJson);

// ── Steps 4–7: Build target-changelog.json ────────────────────────────────────

var root       = JsonNode.Parse(File.ReadAllText(rootJson))!;
var highlights = JsonNode.Parse(File.ReadAllText(highlightsJson))!;
var haAddon    = JsonNode.Parse(File.ReadAllText(haAddonJson))!;

var rootReleases       = root["releases"]!.AsArray();
var highlightsReleases = highlights["releases"]!.AsArray();
var haAddonReleases    = haAddon["releases"]!.AsArray();

var highlightsByVersion = IndexByVersion(highlightsReleases);
var haAddonByVersion    = IndexByVersion(haAddonReleases);

var consumedHaAddon = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var targetReleases  = new JsonArray();

// Step 4 + 5 + 7: process each root version
foreach (var release in rootReleases)
{
    var version = release!["version"]!.GetValue<string>();

    var obj = new JsonObject();
    obj["version"] = version;
    obj["date"]    = release["date"]!.GetValue<string>();

    // highlights ← ha-addon bullets for this version
    if (haAddonByVersion.TryGetValue(version, out var haRelease))
    {
        var haHighlights = haRelease["highlights"]?.AsArray();
        if (haHighlights?.Count > 0)
            obj["highlights"] = haHighlights.DeepClone();
        consumedHaAddon.Add(version);
    }

    // audienceHighlights.ha-addon ← root highlights for this version
    if (highlightsByVersion.TryGetValue(version, out var hlRelease))
    {
        var hlHighlights = hlRelease["highlights"]?.AsArray();
        if (hlHighlights?.Count > 0)
            obj["audienceHighlights"] = new JsonObject { ["ha-addon"] = hlHighlights.DeepClone() };
    }

    // Copy remaining sections from root (no highlights)
    foreach (var key in new[] { "added", "changed", "fixed", "removed", "issues", "cves", "translations" })
    {
        if (release[key] is JsonNode node)
            obj[key] = node.DeepClone();
    }

    targetReleases.Add(obj);
}

// Step 6: append ha-addon versions not present in root
foreach (var haRelease in haAddonReleases)
{
    var version = haRelease!["version"]!.GetValue<string>();
    if (consumedHaAddon.Contains(version)) continue;

    var obj = new JsonObject();
    obj["version"] = version;
    obj["date"]    = haRelease["date"]!.GetValue<string>();

    var haHighlights = haRelease["highlights"]?.AsArray();
    if (haHighlights?.Count > 0)
        obj["highlights"] = haHighlights.DeepClone();

    targetReleases.Add(obj);
}

var targetDoc = new JsonObject { ["releases"] = targetReleases };
var json      = targetDoc.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                         .ReplaceLineEndings("\n");
File.WriteAllText(targetJson, json + "\n");
Console.WriteLine($"Written: {targetJson}");

// ── Helpers ───────────────────────────────────────────────────────────────────

static void RunImport(string importCsx, string format, string extraFlag, string input, string output)
{
    var args = $"\"{importCsx}\" -- --format {format} {extraFlag} --input \"{input}\" --output \"{output}\"";
    var proc = Process.Start(new ProcessStartInfo("dotnet-script", args) { UseShellExecute = false })!;
    proc.WaitForExit();
    if (proc.ExitCode != 0)
    {
        Console.Error.WriteLine($"Import failed (exit {proc.ExitCode}) for: {output}");
        Environment.Exit(1);
    }
}

static void RunImport(string importCsx, string format, string input, string output)
    => RunImport(importCsx, format, "", input, output);

static Dictionary<string, JsonNode> IndexByVersion(JsonArray releases)
{
    var dict = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
    foreach (var r in releases)
    {
        var v = r?["version"]?.GetValue<string>();
        if (v is not null) dict[v] = r!;
    }
    return dict;
}
