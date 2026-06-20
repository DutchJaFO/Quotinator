using System.Text.Json;
using Quotinator.Changelog.Models;

namespace Quotinator.Changelog.Services;

/// <summary>Reads and deserialises <c>changelog.json</c> from <see cref="AppContext.BaseDirectory"/> at startup.</summary>
public sealed class ChangelogService : IChangelogService
{
    /// <inheritdoc/>
    public IReadOnlyList<ChangelogRelease> Releases { get; }

    /// <inheritdoc/>
    public string SourceLanguage { get; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, ChangelogSectionHeaders> SectionHeaders { get; }

    /// <summary>Initialises the service; reads the file if it exists, returns empty data otherwise.</summary>
    public ChangelogService()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "changelog.json");
        if (File.Exists(path))
        {
            var (lang, headers, releases) = Load(path);
            SourceLanguage = lang;
            SectionHeaders = headers;
            Releases       = releases;
        }
        else
        {
            SourceLanguage = "en";
            SectionHeaders = new Dictionary<string, ChangelogSectionHeaders>();
            Releases       = [];
        }
    }

    private static (string SourceLanguage, IReadOnlyDictionary<string, ChangelogSectionHeaders> SectionHeaders, IReadOnlyList<ChangelogRelease> Releases) Load(string path)
    {
        var json = File.ReadAllText(path);
        var dto  = JsonSerializer.Deserialize<ChangelogDto>(json, JsonOptions);
        if (dto is null) return ("en", new Dictionary<string, ChangelogSectionHeaders>(), []);

        var sourceLang = dto.SourceLanguage ?? "en";
        var headers    = BuildSectionHeaders(dto.SectionHeaders);
        var releases   = (dto.Releases ?? [])
            .Where(r => r.Version is not null && r.Date is not null)
            .Select(r => new ChangelogRelease(
                r.Version!,
                r.Date!,
                r.Highlights ?? [],
                BuildSections(r),
                r.Issues ?? [],
                r.Cves ?? [],
                BuildTranslations(r.Translations)))
            .ToList();

        return (sourceLang, headers, releases);
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

    private static IReadOnlyList<ChangelogSection> BuildSections(ReleaseDto r)
    {
        var sections = new List<ChangelogSection>(4);
        if (r.Added   is { Count: > 0 }) sections.Add(new ChangelogSection("Added",   r.Added));
        if (r.Changed is { Count: > 0 }) sections.Add(new ChangelogSection("Changed", r.Changed));
        if (r.Fixed   is { Count: > 0 }) sections.Add(new ChangelogSection("Fixed",   r.Fixed));
        if (r.Removed is { Count: > 0 }) sections.Add(new ChangelogSection("Removed", r.Removed));
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
        List<ReleaseDto>? Releases);

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
