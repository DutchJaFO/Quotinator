using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.BackupMaxExceeded"/> notification (#348): how much
/// the backups folder holds against the maximum it has reached or passed.
/// <para>
/// One condition however many bytes are in use: a folder above its maximum stays the same unresolved
/// error as it grows, until it comes back under. So nothing here identifies it, and one error is open at
/// a time.
/// </para>
/// </summary>
public sealed class BackupMaxExceededMetadataDto() : NotificationMetadataDto(NotificationMetadataKind.BackupMaxExceeded)
{
    /// <summary>What the backups folder held, in bytes.</summary>
    public required long UsedBytes { get; init; }

    /// <summary>The maximum, in bytes: at or above it every backup is refused.</summary>
    public required long CeilingBytes { get; init; }

    /// <summary>Nothing: see the class summary.</summary>
    protected override IEnumerable<object?> IdentityComponents => [];
}
