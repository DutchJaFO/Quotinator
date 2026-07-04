using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>Wire model for a single entry in <c>manifest.json</c>'s <c>files</c> array. See <see cref="ManifestDto"/>.</summary>
internal sealed class ManifestFileEntryDto
{
    /// <summary>Filename relative to this manifest's directory.</summary>
    [JsonPropertyName("file")]
    public required string File { get; init; }

    /// <summary>Human-readable source identifier. Not currently consumed by seeding logic, but part of the schema.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Upstream URL for provenance. Mutually exclusive with <see cref="Github"/> — use <see cref="Github"/> for GitHub-hosted sources instead.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>Direct, fetchable URL used by the auto-update mechanism. Only meaningful alongside <see cref="Url"/> or <see cref="Github"/>.</summary>
    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; init; }

    /// <summary>Overrides <c>Quotinator__SourceUpdateIntervalHours</c> for this specific source.</summary>
    [JsonPropertyName("refreshIntervalHours")]
    public int? RefreshIntervalHours { get; init; }

    /// <summary>Overrides which cache folder the auto-update mechanism writes a downloaded copy of this source to.</summary>
    [JsonPropertyName("downloadTarget")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DownloadTarget? DownloadTarget { get; init; }

    /// <summary>GitHub coordinates this source is fetched from, if hosted there. <see cref="Url"/>/<see cref="DownloadUrl"/> are computed from these, not set directly.</summary>
    [JsonPropertyName("github")]
    public ManifestGithubDto? Github { get; init; }
}
