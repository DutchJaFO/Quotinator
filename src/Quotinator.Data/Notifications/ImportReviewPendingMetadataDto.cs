using System.Text.Json.Serialization;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.ImportReviewPending"/> notification (#303) — which
/// reseeded file left actions awaiting review, which batch they belong to, and how many are in each
/// reviewable state.
/// </summary>
/// <remarks>
/// <b><see cref="BatchId"/> is part of the identity, and that is the whole design.</b> The batch is the
/// set of reviews this alert reports, so two batches are two alerts even for the same file. Because
/// <c>ImportBatchEntity.Id</c> is a fresh <c>Guid.NewGuid()</c> per construction, a later reseed can
/// never reproduce an earlier batch id — so a reseed always raises new alerts rather than silently
/// reusing one describing actions that no longer exist.
/// <para>
/// What stops those accumulating is not dedupe but removal: a reseed truncates <c>Import_Batch</c>, and
/// every alert whose batch went with it is dismissed with the <c>Obsolete</c> reason. An alert
/// therefore only ever survives while the batch it names is still there to be reviewed.
/// </para>
/// </remarks>
public sealed class ImportReviewPendingMetadataDto() : NotificationMetadataDto(NotificationMetadataKind.ImportReviewPending)
{
    /// <summary>The source file whose actions await review, as a bare file name.</summary>
    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    /// <summary>
    /// Which directory the file came from. Part of the identity for the same reason as in
    /// <see cref="ReseedFileAppliedMetadataDto"/>: <see cref="FileName"/> is a bare name, and the
    /// bundled and user-imports directories can both hold one of that name.
    /// </summary>
    [JsonPropertyName("origin")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required FileResourceOrigin Origin { get; init; }

    /// <summary>The <c>Import_Batch</c> row whose actions this alert reports, and what dismissal matches on.</summary>
    [JsonPropertyName("batchId")]
    public required string BatchId { get; init; }

    /// <summary>How many actions are in each reviewable state, omitting any state with none.</summary>
    [JsonPropertyName("counts")]
    public IReadOnlyList<ImportReviewCountDto> Counts { get; init; } = [];

    /// <summary>
    /// The batch, the file it came from, and the review workload. Ordered by status before flattening
    /// for the same reason <see cref="ReseedFileAppliedMetadataDto"/> orders its own breakdown: the
    /// counts are produced by grouping, whose order follows the planner rather than anything stable.
    /// </summary>
    protected override IEnumerable<object?> IdentityComponents =>
    [
        BatchId,
        FileName,
        Origin,
        string.Join('\n', Counts
            .OrderBy(c => c.Status, StringComparer.OrdinalIgnoreCase)
            .Select(c => $"{c.Status}:{c.Count}")),
    ];
}
