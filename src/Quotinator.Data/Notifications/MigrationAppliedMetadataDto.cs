using System.Text.Json.Serialization;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.MigrationApplied"/> notification (#371): which
/// migration ran, and what it did.
/// </summary>
public sealed class MigrationAppliedMetadataDto()
    : NotificationMetadataDto(NotificationMetadataKind.MigrationApplied)
{
    /// <summary>Which of the two migration lists this migration came from.</summary>
    [JsonPropertyName("phase")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required MigrationPhase Phase { get; init; }

    /// <summary>
    /// The migration's own version number within its phase.
    /// </summary>
    /// <remarks>
    /// Deliberately not the base class's <c>Version</c>, which means the <em>release</em> a
    /// notification is about. The two are unrelated: a migration version counts entries in an
    /// append-only list, and no release number characterises it.
    /// </remarks>
    [JsonPropertyName("migrationVersion")]
    public required int MigrationVersion { get; init; }

    /// <summary>What the migration did, taken from <c>SchemaMigration.Description</c>.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// The phase and the version within it: together, exactly one migration.
    /// </summary>
    /// <remarks>
    /// <see cref="Description"/> is deliberately excluded. The two numbers already name one specific
    /// migration, and migrations are append-only and frozen, so including the description would mean a
    /// corrected wording re-announced a migration the operator had already been told about. Versions
    /// alone would not do either: version 5 exists in both lists and means a different migration in
    /// each.
    /// </remarks>
    protected override IEnumerable<object?> IdentityComponents => [Phase, MigrationVersion];
}
