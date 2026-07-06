namespace Quotinator.Data.Import;

/// <summary>
/// Thrown by <see cref="IConflictResolutionCoordinator"/> when an operation isn't valid for a
/// conflict's current <see cref="Entities.SystemImportConflict.Status"/> — e.g. deciding an
/// already-resolved conflict, or undoing one that was never decided.
/// </summary>
public sealed class ConflictStateException : Exception
{
    /// <summary>The conflict id the operation was attempted on.</summary>
    public Guid ConflictId { get; }

    /// <summary>The conflict's actual status at the time of the attempt — one of the <see cref="Entities.ImportConflictStatus"/> constants.</summary>
    public string CurrentStatus { get; }

    /// <summary>Creates the exception with the conflict id and its actual current status.</summary>
    public ConflictStateException(Guid conflictId, string currentStatus)
        : base($"Conflict '{conflictId}' is not in a valid state for this operation (current status: '{currentStatus}').")
    {
        ConflictId    = conflictId;
        CurrentStatus = currentStatus;
    }
}
