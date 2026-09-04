using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>
/// #219: a single quote, by id, to skip entirely during import — for a raw upstream duplicate or
/// unverifiable entry that a <see cref="ConflictResolutionRule"/> or <see cref="SourceAliasRule"/>
/// cannot fix, since those only ever correct a field on a quote that does get imported, never decide
/// whether one should be imported at all.
/// </summary>
public sealed class QuoteExclusionRule
{
    /// <summary>The excluded quote's own id, exactly as it appears in the source file — matched case-insensitively.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Why this quote is excluded, for a future reader auditing the file — not read by any code path.</summary>
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}
