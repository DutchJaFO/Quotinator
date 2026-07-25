using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>The on-disk shape of a per-source conflict-resolution rule file (#181) — one per bundled source, referenced from its manifest entry.</summary>
public sealed class ConflictResolutionRuleFile
{
    /// <summary>Every rule this file declares. Empty for a source with no known recurring conflicts.</summary>
    [JsonPropertyName("rules")]
    public List<ConflictResolutionRule> Rules { get; init; } = [];
}
