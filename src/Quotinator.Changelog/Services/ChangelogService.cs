using System.Text.Json;
using Quotinator.Changelog.Models;

namespace Quotinator.Changelog.Services;

/// <summary>Reads and deserialises <c>changelog.json</c> from <see cref="AppContext.BaseDirectory"/> at startup.</summary>
public sealed class ChangelogService : IChangelogService
{
    /// <inheritdoc/>
    public ChangelogUnreleased? Unreleased { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ChangelogRelease> Releases { get; }

    /// <inheritdoc/>
    public string SourceLanguage { get; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, ChangelogSectionHeaders> SectionHeaders { get; }

    /// <summary>Initialises the service; reads the file if it exists, returns empty data otherwise.</summary>
    public ChangelogService()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "resources", "changelog.json");
        if (!File.Exists(path))
        {
            SourceLanguage = "en";
            SectionHeaders = new Dictionary<string, ChangelogSectionHeaders>();
            Releases       = [];
            return;
        }

        var root = JsonSerializer.Deserialize<ChangelogRoot>(
            File.ReadAllText(path), JsonOptions);

        if (root is null)
        {
            SourceLanguage = "en";
            SectionHeaders = new Dictionary<string, ChangelogSectionHeaders>();
            Releases       = [];
            return;
        }

        SourceLanguage = root.SourceLanguage ?? "en";
        SectionHeaders = root.SectionHeaders ?? new Dictionary<string, ChangelogSectionHeaders>();
        Unreleased     = root.Unreleased;
        Releases       = (root.Releases ?? [])
            .Where(r => !string.IsNullOrEmpty(r.Version) && !string.IsNullOrEmpty(r.Date))
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}
