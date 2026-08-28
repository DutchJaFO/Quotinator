using Quotinator.Data.Enums;

namespace Quotinator.Data.Database;

/// <summary>
/// Signals that a destructive step was abandoned because its safety backup could not be taken (#348).
/// <para>
/// This is a transport, not a reporting mechanism. The refusal reaches a caller as a
/// <see cref="DatabaseOperationResult"/> like any other; an exception is used only to unwind out of
/// <c>OnResetAsync</c>, whose signature belongs to subclasses and has no result to return. Per developer
/// direction an exception is for what there is no other way to detect — here there is no other way to
/// <em>escape</em>, which is a different problem with the same answer.
/// </para>
/// <para>
/// Replaces <c>DatabaseBackupWriteException</c>, which named only the write and could not say which of
/// the five obstacles occurred.
/// </para>
/// </summary>
/// <param name="outcome">Which obstacle stopped the backup.</param>
/// <param name="innerException">The underlying failure, where one was thrown.</param>
public sealed class DatabaseBackupUnavailableException(BackupOutcome outcome, Exception? innerException = null)
    : Exception($"No backup could be taken ({outcome}), so the destructive step was abandoned.", innerException)
{
    /// <summary>Which obstacle stopped the backup.</summary>
    public BackupOutcome Outcome { get; } = outcome;
}
