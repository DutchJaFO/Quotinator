using System.Text.Json;

namespace Quotinator.Data.Import;

/// <summary>
/// Parses the <c>settings</c> multipart text field of <c>POST /api/v1/import</c> and
/// <c>.../import/preview</c> into an <see cref="ImportRequestSettingsDto"/>. Kept as a dedicated,
/// unit-testable seam — mirroring <see cref="ConflictPolicyParser"/> — rather than an inline
/// <c>JsonSerializer.Deserialize</c> call in the endpoint handler, even though the field itself
/// stays a raw string parameter (not natively framework-bound) so a malformed value can still
/// produce a precise <c>422</c> with a localised message instead of a generic framework <c>400</c>.
/// </summary>
public static class ImportRequestSettingsParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Parses <paramref name="json"/> into <paramref name="settings"/>. A missing or blank value
    /// parses successfully to <c>null</c> settings (meaning "use the configured defaults").
    /// Returns <c>false</c> when <paramref name="json"/> is present but not valid JSON in the
    /// expected shape (malformed syntax, or an unrecognised <c>duplicateResolution</c> policy value).
    /// </summary>
    public static bool TryParse(string? json, out ImportRequestSettingsDto? settings)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            settings = null;
            return true;
        }

        try
        {
            settings = JsonSerializer.Deserialize<ImportRequestSettingsDto>(json, Options);
            return true;
        }
        catch (JsonException)
        {
            settings = null;
            return false;
        }
    }
}
