using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>Wire model for <c>manifest.json</c>, deserialized by <see cref="ManifestSeedPlanner"/>. See <c>schemas/manifest.schema.json</c> for the authoritative schema.</summary>
internal sealed class ManifestDto
{
    /// <summary>Optional duplicate-resolution policy overriding application configuration for this directory.</summary>
    [JsonPropertyName("duplicateResolution")]
    public ManifestPolicyDto? DuplicateResolution { get; init; }

    /// <summary>Ordered list of source file entries.</summary>
    [JsonPropertyName("files")]
    public List<ManifestFileEntryDto> Files { get; init; } = [];
}
