using System.Text.Json.Serialization;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.DatabaseActionResult"/> notification (#348): which
/// action ran, how it ended, what it left to restore from, and what stopped it when it was refused.
/// <para>
/// Facts and identity, not prose, per ADR 026 rule 6. The notification's own text is static: it records
/// what was reported to the user when the action ended.
/// </para>
/// </summary>
public sealed class DatabaseActionResultMetadataDto()
    : NotificationMetadataDto(NotificationMetadataKind.DatabaseActionResult)
{
    /// <summary>Identifies this one run of the action, so every run records its own result.</summary>
    [JsonPropertyName("runId")]
    public required Guid RunId { get; init; }

    /// <summary>Which action ran.</summary>
    [JsonPropertyName("action")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required DatabaseAction Action { get; init; }

    /// <summary>How it ended.</summary>
    [JsonPropertyName("outcome")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required DatabaseActionOutcome Outcome { get; init; }

    /// <summary>What a completed action left to restore from; absent when it did not complete.</summary>
    [JsonPropertyName("restorePoint")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RestorePointOutcome? RestorePoint { get; init; }

    /// <summary>The backup taken, or the file set aside; absent otherwise.</summary>
    [JsonPropertyName("fileName")]
    public string? FileName { get; init; }

    /// <summary>Why no backup could be taken: the reason it was skipped, or the reason the action was refused.</summary>
    [JsonPropertyName("backupObstacle")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BackupOutcome? BackupObstacle { get; init; }

    /// <summary>What stopped a Reset that could not replace the database (#482); absent otherwise.</summary>
    [JsonPropertyName("resetObstacle")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ResetObstacle? ResetObstacle { get; init; }

    /// <summary>
    /// The run alone: each one is a separate event the user was told about, and two runs ending the same
    /// way are still two things that happened.
    /// </summary>
    protected override IEnumerable<object?> IdentityComponents => [RunId];
}
