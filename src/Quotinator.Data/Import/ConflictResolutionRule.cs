using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>
/// A single hand-authored rule (#181) that auto-resolves one field of one quote instead of leaving it
/// staged <c>Pending</c> under <see cref="DuplicateResolutionPolicy.Review"/>. Reuses
/// <see cref="FieldResolutionChoice"/>'s existing vocabulary rather than inventing a parallel one — a
/// rule's <see cref="Resolution"/> is applied exactly the way a human's own decide-endpoint choice
/// would be.
/// </summary>
public sealed class ConflictResolutionRule
{
    /// <summary>The quote's own id — matched case-insensitively, per this project's id-comparison convention.</summary>
    [JsonPropertyName("quoteId")]
    public required string QuoteId { get; init; }

    /// <summary>The field name this rule governs (e.g. <c>"date"</c>, <c>"type"</c>, <c>"source"</c>) — matches the consuming entity's own field-map key names (e.g. <c>QuoteFieldMerge</c> for Quote).</summary>
    [JsonPropertyName("field")]
    public required string Field { get; init; }

    /// <summary>How to resolve this field whenever it is genuinely ambiguous. <see cref="FieldResolutionChoice.Custom"/> is not supported by this mechanism.</summary>
    [JsonPropertyName("resolution")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required FieldResolutionChoice Resolution { get; init; }
}
