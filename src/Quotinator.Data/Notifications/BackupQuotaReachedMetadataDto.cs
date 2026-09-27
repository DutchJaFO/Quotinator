using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.BackupQuotaReached"/> notification (#348): how much
/// the backups folder holds against its operating quota and its ceiling, when the warning was raised.
/// <para>
/// One condition however many bytes are in use: a folder past its quota stays the same unresolved
/// warning as it grows or shrinks, until it comes back under. So nothing here identifies it, and one warning
/// is open at a time.
/// </para>
/// </summary>
public sealed class BackupQuotaReachedMetadataDto() : NotificationMetadataDto(NotificationMetadataKind.BackupQuotaReached)
{
    /// <summary>What the backups folder held, in bytes.</summary>
    public required long UsedBytes { get; init; }

    /// <summary>The operating quota, in bytes: above it a backup is still taken, with this warning.</summary>
    public required long QuotaBytes { get; init; }

    /// <summary>The ceiling, in bytes: a backup that would pass it is refused.</summary>
    public required long CeilingBytes { get; init; }

    /// <summary>Nothing: see the class summary.</summary>
    protected override IEnumerable<object?> IdentityComponents => [];
}
