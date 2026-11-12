using System.Text.Json.Serialization;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;

namespace Quotinator.Core.Models;

/// <summary>
/// What a Reset left behind to restore from (#348), as the caller reads it: the backup it took, the
/// unreadable file it set aside, or that it took none and why. The caller that asked for the Reset is the
/// one that has to know there is no restore point, so it is told in the response rather than left to find
/// it in a log or the audit trail.
/// </summary>
public sealed class RestorePointDto
{
    /// <summary><c>Succeeded</c>, <c>Skipped</c> or <c>SetAside</c>.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required RestorePointOutcome Outcome { get; init; }

    /// <summary>The backup's file name, or the set-aside file's; absent when the backup was skipped.</summary>
    public string? Name { get; init; }

    /// <summary>Why no backup could be taken; present only when it was skipped.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BackupOutcome? Obstacle { get; init; }

    /// <summary>The caller's view of an operation's restore point, or <see langword="null"/> when it reported none.</summary>
    /// <param name="restorePoint">What the operation left behind to restore from.</param>
    public static RestorePointDto? From(RestorePointResult? restorePoint) =>
        restorePoint is null
            ? null
            : new RestorePointDto { Outcome = restorePoint.Outcome, Name = restorePoint.FileName, Obstacle = restorePoint.Obstacle };
}
