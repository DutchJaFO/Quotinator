using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>Wire model for a <c>github</c> entry within <see cref="ManifestFileEntryDto"/>.</summary>
internal sealed class ManifestGithubDto
{
    /// <summary>GitHub repository owner (user or organisation).</summary>
    [JsonPropertyName("owner")]
    public required string Owner { get; init; }

    /// <summary>GitHub repository name.</summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>File path within the repo, relative to its root.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>Branch or tag to fetch from. Defaults to <c>main</c> when omitted.</summary>
    [JsonPropertyName("branch")]
    public string Branch { get; init; } = "main";
}
