using System.Text.Json.Serialization;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.BackupRefused"/> notification (#348): which step
/// refused to run, and the obstacle that stopped its backup.
/// <para>
/// The obstacle is what decides which options the notification can offer, so it is data the executor
/// reads, not prose. The same refusal recurring on every start is the same unresolved condition, while a
/// different obstacle is a different one with different remedies, so both values identify it.
/// </para>
/// </summary>
public sealed class BackupRefusedMetadataDto() : NotificationMetadataDto(NotificationMetadataKind.BackupRefused)
{
    /// <summary>The step that did not run. Stored by name, so reordering the enum cannot change what a stored row means.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required BackupGuardedStep Step { get; init; }

    /// <summary>What stopped the backup that step needed. Stored by name, for the same reason.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required BackupOutcome Obstacle { get; init; }

    /// <summary>The step plus the obstacle.</summary>
    protected override IEnumerable<object?> IdentityComponents => [Step, Obstacle];
}
