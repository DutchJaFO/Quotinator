using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>
/// Wire model for the <c>settings</c> multipart field of <c>POST /api/v1/import</c> and
/// <c>POST /api/v1/import/preview</c>. Adds <see cref="Enrich"/> to the shared
/// <see cref="SourceImportSettingsDto"/> base — deliberately not added to that base itself, since
/// <c>enrich</c> is a request-only concept with no meaning inside <c>manifest.json</c>.
/// </summary>
public sealed class ImportRequestSettingsDto : SourceImportSettingsDto
{
    /// <summary>
    /// When <c>true</c>, requests an enrichment pass after import. Not yet implemented — the
    /// endpoint returns <c>501 Not Implemented</c> when this is <c>true</c>, deferred to #19.
    /// Defaults to <c>false</c> (no-op) when omitted.
    /// </summary>
    [JsonPropertyName("enrich")]
    public bool Enrich { get; init; }
}
