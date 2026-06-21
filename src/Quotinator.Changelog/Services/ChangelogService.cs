using System.Text.Json;
using Quotinator.Changelog.Models;

namespace Quotinator.Changelog.Services;

/// <summary>Reads and deserialises <c>changelog.json</c> from <see cref="AppContext.BaseDirectory"/> at startup.</summary>
public sealed class ChangelogService : IChangelogService
{
    /// <inheritdoc/>
    public ChangelogRelease? Unreleased { get; }

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
        if (File.Exists(path))
        {
            var (lang, headers, unreleased, releases) = Load(path);
            SourceLanguage = lang;
            SectionHeaders = headers;
            Unreleased     = unreleased;
            Releases       = releases;
        }
        else
        {
            SourceLanguage = "en";
            SectionHeaders = new Dictionary<string, ChangelogSectionHeaders>();
            Releases       = [];
        }
    }

    private static (string SourceLanguage, IReadOnlyDictionary<string, ChangelogSectionHeaders> SectionHeaders, ChangelogRelease? Unreleased, IReadOnlyList<ChangelogRelease> Releases) Load(string path)
    {
        var json = File.ReadAllText(path);
        var dto  = JsonSerializer.Deserialize<ChangelogDto>(json, JsonOptions);
        if (dto is null) return ("en", new Dictionary<string, ChangelogSectionHeaders>(), null, []);

        var sourceLang = dto.SourceLanguage ?? "en";
        var headers    = BuildSectionHeaders(dto.SectionHeaders);

        var unreleased = dto.Unreleased is { } u
            ? new ChangelogRelease(
                "", "",
                u.Highlights ?? [],
                BuildSections(u.Added, u.Changed, u.Fixed, u.Removed),
                u.Issues ?? [],
                u.Cves ?? [],
                new Dictionary<string, ChangelogReleaseTranslation>())
            : null;

        var releases = (dto.Releases ?? [])
            .Where(r => r.Version is not null && r.Date is not null)
            .Select(r => new ChangelogRelease(
                r.Version!,
                r.Date!,
                r.Highlights ?? [],
                BuildSections(r.Added, r.Changed, r.Fixed, r.Removed),
                r.Issues ?? [],
                r.Cves ?? [],
                BuildTranslations(r.Translations)))
            .ToList();

        return (sourceLang, headers, unreleased, releases);
    }

    private static IReadOnlyDictionary<string, ChangelogSectionHeaders> BuildSectionHeaders(
        Dictionary<string, Dictionary<string, string>>? dto)
    {
        if (dto is null) return new Dictionary<string, ChangelogSectionHeaders>();
        return dto.ToDictionary(
            kvp => kvp.Key,
            kvp => new ChangelogSectionHeaders(
                kvp.Value.GetValueOrDefault("highlights", ""),
                kvp.Value.GetValueOrDefault("added",      ""),
                kvp.Value.GetValueOrDefault("changed",    ""),
                kvp.Value.GetValueOrDefault("fixed",      ""),
                kvp.Value.GetValueOrDefault("removed",    "")));
    }

    private static IReadOnlyList<ChangelogSection> BuildSections(
        List<string>? added, List<string>? changed, List<string>? fixed_, List<string>? removed)
    {
        var sections = new List<ChangelogSection>(4);
        if (added   is { Count: > 0 }) sections.Add(new ChangelogSection("Added",   added));
        if (changed is { Count: > 0 }) sections.Add(new ChangelogSection("Changed", changed));
        if (fixed_  is { Count: > 0 }) sections.Add(new ChangelogSection("Fixed",   fixed_));
        if (removed is { Count: > 0 }) sections.Add(new ChangelogSection("Removed", removed));
        return sections;
    }

    private static IReadOnlyDictionary<string, ChangelogReleaseTranslation> BuildTranslations(
        Dictionary<string, TranslationDto>? translations)
    {
        if (translations is null) return new Dictionary<string, ChangelogReleaseTranslation>();
        return translations.ToDictionary(
            kvp => kvp.Key,
            kvp => new ChangelogReleaseTranslation(
                MapItems(kvp.Value.Highlights),
                MapItems(kvp.Value.Added),
                MapItems(kvp.Value.Changed),
                MapItems(kvp.Value.Fixed),
                MapItems(kvp.Value.Removed)));
    }

    private static IReadOnlyList<ChangelogTranslationItem> MapItems(List<TranslationItemDto>? items)
        => (items ?? [])
            .Where(i => !string.IsNullOrEmpty(i.Text))
            .Select(i => new ChangelogTranslationItem(i.Text!, i.MachineTranslated))
            .ToList();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record ChangelogDto(
        string? SourceLanguage,
        Dictionary<string, Dictionary<string, string>>? SectionHeaders,
        UnreleasedDto? Unreleased,
        List<ReleaseDto>? Releases);

    private sealed record UnreleasedDto(
        List<string>? Highlights,
        List<string>? Added,
        List<string>? Changed,
        List<string>? Fixed,
        List<string>? Removed,
        List<int>? Issues,
        List<string>? Cves);

    private sealed record ReleaseDto(
        string? Version,
        string? Date,
        List<string>? Highlights,
        List<string>? Added,
        List<string>? Changed,
        List<string>? Fixed,
        List<string>? Removed,
        List<int>? Issues,
        List<string>? Cves,
        Dictionary<string, TranslationDto>? Translations);

    private sealed record TranslationDto(
        List<TranslationItemDto>? Highlights,
        List<TranslationItemDto>? Added,
        List<TranslationItemDto>? Changed,
        List<TranslationItemDto>? Fixed,
        List<TranslationItemDto>? Removed);

    private sealed record TranslationItemDto(string? Text, bool? MachineTranslated);
}
