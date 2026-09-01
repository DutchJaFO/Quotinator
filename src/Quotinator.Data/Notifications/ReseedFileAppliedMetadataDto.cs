using System.Text.Json.Serialization;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.ReseedFileApplied"/> notification (#302) — which
/// source file reseeded with nothing left to review, and what it actually did, per entity type.
/// <para>
/// The file name and the breakdown together identify the notification. That is what makes reseeding
/// twice with nothing changed in between produce one confirmation rather than two, while a file whose
/// result genuinely differs notifies separately instead of being suppressed as a duplicate.
/// </para>
/// </summary>
public sealed class ReseedFileAppliedMetadataDto() : NotificationMetadataDto(NotificationMetadataKind.ReseedFileApplied)
{
    /// <summary>The source file this confirms, as a bare file name.</summary>
    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    /// <summary>
    /// What the file did, one entry per entity type that added or modified at least one row. Empty is a
    /// legitimate value in the payload's own terms, though the producer never writes one — a file that
    /// changed nothing still applied cleanly.
    /// </summary>
    [JsonPropertyName("counts")]
    public IReadOnlyList<ReseedEntityCountDto> Counts { get; init; } = [];

    /// <summary>
    /// The file name plus its breakdown. Flattened to scalars because the base type compares identity
    /// as a sequence of them, with separators that cannot occur in a file name or an entity type.
    /// <para>
    /// Ordered by entity type before flattening, and that is load-bearing rather than tidiness: the
    /// producer builds these by grouping the file's import actions, so the order follows whatever order
    /// the planner emitted them in. Left as produced, one unchanged file could yield two different
    /// identities across two reseeds and re-announce a confirmation the operator already dismissed.
    /// </para>
    /// </summary>
    protected override IEnumerable<object?> IdentityComponents =>
    [
        FileName,
        string.Join('\n', Counts
            .OrderBy(c => c.EntityType, StringComparer.OrdinalIgnoreCase)
            .Select(c => $"{c.EntityType}:{c.Added}:{c.Modified}")),
    ];
}
