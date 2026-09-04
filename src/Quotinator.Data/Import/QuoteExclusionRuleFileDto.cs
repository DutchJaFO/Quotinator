using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>The on-disk shape of a per-source quote-exclusion file (#219) — one per bundled source, referenced from its manifest entry.</summary>
public sealed class QuoteExclusionRuleFileDto
{
    /// <summary>Every quote this file excludes. Empty for a source with no known exclusions.</summary>
    [JsonPropertyName("exclusions")]
    public List<QuoteExclusionRule> Exclusions { get; init; } = [];
}
