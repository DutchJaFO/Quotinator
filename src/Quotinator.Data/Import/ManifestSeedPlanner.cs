using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Quotinator.Data.Import;

/// <inheritdoc/>
public sealed class ManifestSeedPlanner(ILogger<ManifestSeedPlanner> logger) : IManifestSeedPlanner
{
    private const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
            var manifest = JsonSerializer.Deserialize<ManifestDto>(File.ReadAllText(manifestPath), ReadOptions)
                ?? throw new JsonException($"{manifestPath} deserialized to null");

            var fromManifest   = manifest.DuplicateResolution is null ? null : ToManifestPolicy(manifest.DuplicateResolution);
            var resolvedPolicy = ManifestPolicy.Resolve(fromManifest, configPolicy);

            var listed = manifest.Files
                .Select(e =>
                {
                    var path               = Path.Combine(dir, e.File);
                    var (url, downloadUrl) = ResolveUrls(e);
                    return new SeedFile(path, url, downloadUrl, e.RefreshIntervalHours, e.DownloadTarget, e.Converter);
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

    private static (string? Url, string? DownloadUrl) ResolveUrls(ManifestFileEntryDto entry)
    {
        if (entry.Github is not null)
        {
            return (
                $"https://github.com/{entry.Github.Owner}/{entry.Github.Repo}",
                $"https://raw.githubusercontent.com/{entry.Github.Owner}/{entry.Github.Repo}/{entry.Github.Branch}/{entry.Github.Path}");
        }

        return (entry.Url, entry.DownloadUrl);
    }

    private static ManifestPolicy ToManifestPolicy(ManifestPolicyDto dto) => new(
        Default:      dto.Default,
        Quotes:       dto.Quotes,
        Sources:      dto.Sources,
        Characters:   dto.Characters,
        People:       dto.People,
        Translations: dto.Translations);

    private void TryWriteAutoManifest(string manifestPath, IReadOnlyList<SeedFile> files)
    {
        try
        {
            var manifest = new ManifestDto
            {
                Files = files
                    .Select(f => new ManifestFileEntryDto
                    {
                        File = Path.GetFileName(f.FilePath),
                        Name = Path.GetFileName(f.FilePath)
                    })
                    .ToList()
            };

            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, WriteOptions));
            logger.LogWarning("[Database - Init] no manifest found in {Dir} — auto-created manifest.json listing {Count} file(s) alphabetically",
                Path.GetDirectoryName(manifestPath), files.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Database - Init] failed to auto-create manifest at {Path} — continuing without one", manifestPath);
        }
    }
}
