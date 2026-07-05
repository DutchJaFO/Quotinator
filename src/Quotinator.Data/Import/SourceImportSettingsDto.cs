using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>
/// Shared wire-model base for the two settings a single quote source needs: which converter (if
/// any) transforms its raw format into Quotinator's canonical schema, and which duplicate-resolution
/// policy governs it. Used both as a per-file override on <see cref="ManifestFileEntryDto"/> and,
/// via <c>ImportRequestSettingsDto</c> in <c>Quotinator.Api</c>, as the settings blob for the
/// <c>POST /api/v1/quotes/import</c> endpoint.
/// </summary>
public class SourceImportSettingsDto
{
    /// <summary>
    /// Name of a compiled <c>IQuoteSourceConverter</c> plugin (matched by its <c>Name</c> property)
    /// that converts this source's raw upstream format to Quotinator's canonical schema. Omitted
    /// means the source is already in canonical schema and needs no conversion.
    /// </summary>
    [JsonPropertyName("converter")]
    public string? Converter { get; init; }

    /// <summary>
    /// Duplicate-resolution policy overriding the next tier down (a manifest's own top-level policy,
    /// or application configuration) for this source specifically. Omitted means fall through to the
    /// next tier unchanged.
    /// </summary>
    [JsonPropertyName("duplicateResolution")]
    public ManifestPolicyDto? DuplicateResolution { get; init; }
}
