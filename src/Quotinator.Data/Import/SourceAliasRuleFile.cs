using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>The on-disk shape of a per-source title-alias file (#181) — one per bundled source, referenced from its manifest entry.</summary>
public sealed class SourceAliasRuleFile
{
    /// <summary>Every alias this file declares. Empty for a source with no known title/type inconsistencies.</summary>
    [JsonPropertyName("aliases")]
    public List<SourceAliasRule> Aliases { get; init; } = [];
}
