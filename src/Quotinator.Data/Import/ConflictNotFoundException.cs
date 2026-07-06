namespace Quotinator.Data.Import;

/// <summary>Thrown by <see cref="IConflictResolutionCoordinator"/> when a conflict id does not exist.</summary>
public sealed class ConflictNotFoundException : Exception
{
    /// <summary>The conflict id that was not found.</summary>
    public Guid ConflictId { get; }

    /// <summary>Creates the exception for the given missing conflict id.</summary>
    public ConflictNotFoundException(Guid conflictId)
        : base($"Conflict '{conflictId}' does not exist.")
    {
        ConflictId = conflictId;
    }
}
