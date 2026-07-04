using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>Wire model for the <c>duplicateResolution</c> section of <c>manifest.json</c>. See <see cref="ManifestDto"/>.</summary>
internal sealed class ManifestPolicyDto
{
    /// <summary>Policy applied to all entity types that do not have a type-specific override. Defaults to <see cref="DuplicateResolutionPolicy.Skip"/> when omitted.</summary>
    [JsonPropertyName("default")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DuplicateResolutionPolicy Default { get; init; } = DuplicateResolutionPolicy.Skip;

    /// <summary>Override for quote rows. Null means use <see cref="Default"/>.</summary>
    [JsonPropertyName("quotes")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DuplicateResolutionPolicy? Quotes { get; init; }

    /// <summary>Override for source rows. Null means use <see cref="Default"/>.</summary>
    [JsonPropertyName("sources")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DuplicateResolutionPolicy? Sources { get; init; }

    /// <summary>Override for character rows. Null means use <see cref="Default"/>.</summary>
    [JsonPropertyName("characters")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DuplicateResolutionPolicy? Characters { get; init; }

    /// <summary>Override for people rows. Null means use <see cref="Default"/>.</summary>
    [JsonPropertyName("people")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DuplicateResolutionPolicy? People { get; init; }

    /// <summary>Override for translation rows. Null means use <see cref="Default"/>.</summary>
    [JsonPropertyName("translations")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DuplicateResolutionPolicy? Translations { get; init; }
}
