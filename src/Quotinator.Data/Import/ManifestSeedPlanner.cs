using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Quotinator.Data.Import;

/// <inheritdoc/>
public sealed class ManifestSeedPlanner(ILogger<ManifestSeedPlanner> logger) : IManifestSeedPlanner
{
    private const string ManifestFileName = "manifest.json";

    /// <inheritdoc/>
    public (IReadOnlyList<SeedFile> Files, ManifestPolicy Policy) PlanSeed(
        string dir, ManifestPolicy configPolicy, bool allowAutoCreate)
    {
        var allJson = Directory.GetFiles(dir, "*.json")
            .Where(f => !Path.GetFileName(f).Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => new SeedFile(f, null))
            .ToList();

        var manifestPath = Path.Combine(dir, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            if (allowAutoCreate && allJson.Count > 0)
                TryWriteAutoManifest(manifestPath, allJson);
            else
                logger.LogInformation("[Database - Init] no manifest in {Dir} — importing {Count} JSON file(s) in alphabetical order", dir, allJson.Count);

            return (allJson, configPolicy);
        }

        try
        {
            var root                = JsonNode.Parse(File.ReadAllText(manifestPath));
            var manifestPolicyNode  = root?["duplicateResolution"];
            var fromManifest        = manifestPolicyNode is null ? null : ParseManifestPolicyNode(manifestPolicyNode);
            var resolvedPolicy      = ManifestPolicy.Resolve(fromManifest, configPolicy);

            var listed = (root?["files"]?.AsArray() ?? [])
                .Select(e =>
                {
                    var path = Path.Combine(dir, e!["file"]!.GetValue<string>());
                    var (url, downloadUrl) = ResolveUrls(e);
                    return new SeedFile(path, url, downloadUrl);
                })
                .Where(f => File.Exists(f.FilePath))
                .ToList();

            var listedPaths = new HashSet<string>(listed.Select(f => f.FilePath), StringComparer.OrdinalIgnoreCase);
            var unlisted     = allJson.Where(f => !listedPaths.Contains(f.FilePath)).ToList();
            if (unlisted.Count > 0)
                logger.LogInformation("[Database - Init] {Count} file(s) not listed in manifest will be appended: {Files}",
                    unlisted.Count, string.Join(", ", unlisted.Select(f => Path.GetFileName(f.FilePath))));

            return ([.. listed, .. unlisted], resolvedPolicy);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Database - Init] failed to read manifest at {Path} — falling back to alphabetical order", manifestPath);
            return (allJson, configPolicy);
        }
    }

    private static (string? Url, string? DownloadUrl) ResolveUrls(JsonNode entry)
    {
        var githubNode = entry["github"];
        if (githubNode is not null)
        {
            var owner  = githubNode["owner"]!.GetValue<string>();
            var repo   = githubNode["repo"]!.GetValue<string>();
            var path   = githubNode["path"]!.GetValue<string>();
            var branch = githubNode["branch"]?.GetValue<string>() ?? "main";

            return (
                $"https://github.com/{owner}/{repo}",
                $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}");
        }

        var url         = entry["url"]?.GetValue<string>();
        var downloadUrl = entry["downloadUrl"]?.GetValue<string>();
        return (url, downloadUrl);
    }

    private void TryWriteAutoManifest(string manifestPath, IReadOnlyList<SeedFile> files)
    {
        try
        {
            var manifest = new JsonObject
            {
                ["files"] = new JsonArray(files
                    .Select(f => (JsonNode)new JsonObject
                    {
                        ["file"] = Path.GetFileName(f.FilePath),
                        ["name"] = Path.GetFileName(f.FilePath)
                    })
                    .ToArray())
            };

            File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            logger.LogWarning("[Database - Init] no manifest found in {Dir} — auto-created manifest.json listing {Count} file(s) alphabetically",
                Path.GetDirectoryName(manifestPath), files.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Database - Init] failed to auto-create manifest at {Path} — continuing without one", manifestPath);
        }
    }

    private static ManifestPolicy ParseManifestPolicyNode(JsonNode node)
    {
        static DuplicateResolutionPolicy ParsePol(JsonNode? n) =>
            n?.GetValue<string>().ToLowerInvariant() == "overwrite"
                ? DuplicateResolutionPolicy.Overwrite
                : DuplicateResolutionPolicy.Skip;

        static DuplicateResolutionPolicy? ParseNullPol(JsonNode? n) =>
            n?.GetValue<string>().ToLowerInvariant() switch
            {
                "overwrite" => DuplicateResolutionPolicy.Overwrite,
                "skip"      => DuplicateResolutionPolicy.Skip,
                _           => null
            };

        return new ManifestPolicy(
            Default:      ParsePol(node["default"]),
            Quotes:       ParseNullPol(node["quotes"]),
            Sources:      ParseNullPol(node["sources"]),
            Characters:   ParseNullPol(node["characters"]),
            People:       ParseNullPol(node["people"]),
            Translations: ParseNullPol(node["translations"]));
    }
}
